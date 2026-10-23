using System;
using AutoChess.Core.Combat;

namespace AutoChess.Core {
    /// <summary>
    /// The authoritative game: owns the <see cref="MatchState"/>, advances it in fixed ticks and applies
    /// player commands (was GameManager + Progress + the feature MonoBehaviours). Engine-free and
    /// deterministic: the same seed and the same commands at the same ticks give the same match, which is
    /// what a dedicated server, a lockstep client, replays and the regression tests need.
    ///
    /// Phase loop (durations in <see cref="GameRules"/>):
    /// Setup (starter pick) -> Preparation -> Transition (boards auto-filled, battles built) -> Battle
    /// (until every battle ends or time runs out) -> Summary (rewards decided) -> Preparation (+xp,
    /// rewards granted, merges resolved, shop refreshed) ... -> Finished after the last round.
    /// </summary>
    public sealed class MatchSimulation {
        public readonly GameDatabase Db;
        public readonly MatchConfig Config;
        public readonly MatchState State = new();
        public readonly EventBuffer Events = new();

        readonly MatchContext ctx;
        readonly DeterministicRandom rng;
        readonly AbilityRegistry abilities;

        public GameRules Rules => Db.Rules;
        public MatchPhase Phase => State.Phase;
        public float PhaseTimeLeft => State.PhaseTicksLeft * Rules.TickDelta;
        public float PhaseDuration => State.PhaseTicksTotal * Rules.TickDelta;
        public bool BoardLocked => State.Phase != MatchPhase.Preparation && State.Phase != MatchPhase.Setup;

        public MatchSimulation(GameDatabase db, MatchConfig config) {
            Db = db ?? throw new ArgumentNullException(nameof(db));
            Config = config ?? new MatchConfig();
            rng = new DeterministicRandom(Config.Seed);
            abilities = Config.Abilities ?? AbilityRegistry.Default;
            ctx = new MatchContext(db, rng, Events, Config.Pool ?? new UnlimitedUnitPool(db)) { State = State };

            var count = Math.Max(1, Config.PlayerCount);
            for (var i = 0; i < count; i++) {
                var p = new PlayerState(i, db.Rules) {
                    Name = Config.PlayerNames != null && i < Config.PlayerNames.Length ? Config.PlayerNames[i] : $"Player {i + 1}",
                    Rng = rng.Fork((uint)(i + 1)),
                };
                State.Players.Add(p);
            }
        }

        public PlayerState Player(int id) => State.Player(id);

        public RoundDefinition CurrentRound {
            get {
                var stages = Db.Stages;
                if (State.StageIndex >= stages.Count) return null;
                var rounds = stages[State.StageIndex].Rounds;
                return State.RoundIndex < rounds.Length ? rounds[State.RoundIndex] : null;
            }
        }

        // ------------------------------------------------------------------ clock

        public void Start() {
            if (State.Phase != MatchPhase.None) return;
            Events.Tick = State.Tick;
            foreach (var p in State.Players) {
                Events.Add(new GoldChangedEvent { PlayerId = p.Id, Gold = p.Gold });
                Events.Add(new XpChangedEvent { PlayerId = p.Id, Level = p.Level, Xp = p.Xp, XpToNext = Economy.XpToNext(Rules, p) });
                Roster.BoardChanged(ctx, p);
            }
            if (Config.SkipStarterPack || Db.ShopPool(Reputation.Unknown).Count == 0) {
                foreach (var p in State.Players) p.StarterChosen = true;
                EnterPreparation(true);
            }
            else {
                SetPhase(MatchPhase.Setup, Rules.SetupSeconds);
                foreach (var p in State.Players) Rewards.RollStarterOffers(ctx, p);
            }
        }

        /// <summary>Advances the match by one tick (1 / TicksPerSecond seconds).</summary>
        public void Tick() {
            if (State.Phase == MatchPhase.None || State.Phase == MatchPhase.Finished) return;
            State.Tick++;
            Events.Tick = State.Tick;

            if (State.Phase == MatchPhase.Battle) {
                var allDone = true;
                foreach (var battle in State.Battles.Values) {
                    battle.Step();
                    allDone &= battle.Finished;
                }
                if (allDone) {
                    EnterSummary();
                    return;
                }
            }

            if (--State.PhaseTicksLeft <= 0) Advance();
        }

        void Advance() {
            switch (State.Phase) {
                case MatchPhase.Setup:
                    foreach (var p in State.Players) {
                        if (!p.StarterChosen) Rewards.ChooseStarterOffer(ctx, p, 0);
                    }
                    EnterPreparation(true);
                    break;
                case MatchPhase.Preparation:
                    EnterTransition();
                    break;
                case MatchPhase.Transition:
                    EnterBattle();
                    break;
                case MatchPhase.Battle:
                    foreach (var battle in State.Battles.Values) battle.ForceEnd(BattleOutcome.Draw);
                    EnterSummary();
                    break;
                case MatchPhase.Summary:
                    NextRound();
                    break;
            }
        }

        void SetPhase(MatchPhase phase, float seconds) {
            var previous = State.Phase;
            State.Phase = phase;
            State.PhaseTicksTotal = State.PhaseTicksLeft = Math.Max(1, Rules.SecondsToTicks(seconds));
            Events.Add(new PhaseChangedEvent {
                Phase = phase, Previous = previous, DurationTicks = State.PhaseTicksTotal,
                Stage = State.StageIndex, Round = State.RoundIndex
            });
        }

        void EnterPreparation(bool first) {
            if (CurrentRound == null) {
                Finish();
                return;
            }
            if (!first) {
                foreach (var p in State.Players) Economy.GainXp(ctx, p, Rules.XpPerRound);
            }
            State.Battles.Clear();
            SetPhase(MatchPhase.Preparation, Rules.PreparationSeconds);
            foreach (var p in State.Players) {
                p.Ready = false;
                Rewards.GrantPending(ctx, p);
                Roster.ResolveMerges(ctx, p);
                Shop.AutoRefresh(ctx, p);
            }
        }

        void EnterTransition() {
            SetPhase(MatchPhase.Transition, Rules.TransitionSeconds);
            var round = CurrentRound;
            foreach (var p in State.Players) {
                Roster.AutoFillBoard(ctx, p);
                var setup = new BattleSetup {
                    Seed = ((ulong)rng.NextUInt() << 32) | rng.NextUInt(),
                    Home = BattleSetups.FromBoard(p),
                    Away = BattleSetups.FromRound(round),
                };
                var battle = new BattleSimulation(++State.LastBattleId, Db, setup, Events, abilities);
                State.Battles[p.Id] = battle;
                Events.Add(new BattleCreatedEvent { PlayerId = p.Id, BattleId = battle.Id });
            }
        }

        void EnterBattle() {
            SetPhase(MatchPhase.Battle, Rules.BattleSeconds);
            var allDone = true;
            foreach (var battle in State.Battles.Values) {
                battle.Start();
                allDone &= battle.Finished;
            }
            if (allDone) EnterSummary();
        }

        void EnterSummary() {
            SetPhase(MatchPhase.Summary, Rules.SummarySeconds);
            var round = CurrentRound;
            foreach (var p in State.Players) {
                var battle = State.BattleOf(p.Id);
                var outcome = battle?.Outcome ?? BattleOutcome.Draw;
                if (outcome == BattleOutcome.None) outcome = BattleOutcome.Draw;
                p.LastOutcome = outcome;
                if (outcome == BattleOutcome.HomeWin) p.Wins++;
                else p.Losses++;

                p.PendingRewards.Clear();
                if (round != null) {
                    p.PendingRewards.AddRange(round.Rewards);
                    if (outcome == BattleOutcome.HomeWin) p.PendingRewards.AddRange(round.WinRewards);
                }
                Events.Add(new BattleFinishedEvent { PlayerId = p.Id, BattleId = battle?.Id ?? 0, Outcome = outcome });
            }
        }

        void NextRound() {
            State.RoundIndex++;
            var stages = Db.Stages;
            if (State.StageIndex < stages.Count && State.RoundIndex >= stages[State.StageIndex].Rounds.Length) {
                State.RoundIndex = 0;
                State.StageIndex++;
            }
            if (State.StageIndex >= stages.Count) {
                Finish();
                return;
            }
            EnterPreparation(false);
        }

        void Finish() {
            State.Battles.Clear();
            SetPhase(MatchPhase.Finished, 0);
            Events.Add(new MatchFinishedEvent());
        }

        void ShortenPhase() {
            var ticks = Math.Max(1, Rules.SecondsToTicks(Rules.ReadyRemainingSeconds));
            if (State.PhaseTicksLeft > ticks) State.PhaseTicksLeft = ticks;
        }

        // ------------------------------------------------------------------ commands

        public CommandResult Submit(GameCommand command) {
            if (command == null) return CommandResult.Fail(CommandError.Unsupported, "null command");
            Events.Tick = State.Tick;
            var p = State.Player(command.PlayerId);
            if (p == null) return CommandError.InvalidPlayer;
            if (command.IsCheat && !Config.AllowCheats) return CommandError.CheatsDisabled;

            var phase = State.Phase;
            var inGame = phase is MatchPhase.Preparation or MatchPhase.Transition or MatchPhase.Battle or MatchPhase.Summary;

            switch (command) {
                case ChooseStarterCommand c: {
                    if (phase != MatchPhase.Setup) return CommandError.WrongPhase;
                    var r = Rewards.ChooseStarterOffer(ctx, p, c.OfferIndex);
                    if (r.Ok && State.Players.TrueForAll(x => x.StarterChosen)) EnterPreparation(true);
                    return r;
                }
                case RerollStarterCommand c:
                    if (phase != MatchPhase.Setup) return CommandError.WrongPhase;
                    return Rewards.RerollStarterOffer(ctx, p, c.OfferIndex);

                case BuyUnitCommand c:
                    if (!inGame) return CommandError.WrongPhase;
                    return Shop.Buy(ctx, p, c.ShopSlot, BoardLocked);

                case RefreshShopCommand _:
                    if (!inGame) return CommandError.WrongPhase;
                    return Shop.Refresh(ctx, p);

                case SetShopLockCommand c:
                    if (!inGame) return CommandError.WrongPhase;
                    Shop.SetLocked(ctx, p, c.Locked);
                    return CommandResult.Success;

                case BuyXpCommand _:
                    if (!inGame) return CommandError.WrongPhase;
                    return Economy.BuyXp(ctx, p);

                case MoveUnitCommand c:
                    if (phase != MatchPhase.Preparation) return CommandError.WrongPhase;
                    return Roster.Move(ctx, p, p.FindUnit(c.UnitId), c.Target);

                case EquipItemCommand c:
                    if (!inGame) return CommandError.WrongPhase;
                    return Items.EquipFromBench(ctx, p, c.ItemId, p.FindUnit(c.UnitId), BoardLocked);

                case ReadyCommand _:
                    if (phase != MatchPhase.Preparation) return CommandError.WrongPhase;
                    p.Ready = true;
                    if (State.Players.TrueForAll(x => x.Ready)) ShortenPhase();
                    return CommandResult.Success;

                case CheatGoldCommand c:
                    Economy.GainGold(ctx, p, c.Amount);
                    return CommandResult.Success;

                case CheatXpCommand c:
                    return Economy.GainXp(ctx, p, c.Amount) ? CommandResult.Success : CommandError.MaxLevel;

                case CheatItemCommand c:
                    if (!Db.TryGetItem(c.ItemId, out _)) return CommandError.InvalidItem;
                    Items.AddToBench(ctx, p, c.ItemId, Math.Max(1, c.Count));
                    return CommandResult.Success;

                case CheatUnitCommand c:
                    return CheatUnit(p, c);

                case CheatSkipPhaseCommand _:
                    if (phase is MatchPhase.None or MatchPhase.Finished) return CommandError.WrongPhase;
                    ShortenPhase();
                    return CommandResult.Success;

                case CheatSetShopCommand c:
                    return CheatSetShop(p, c);

                default:
                    return CommandResult.Fail(CommandError.Unsupported, command.GetType().Name);
            }
        }

        CommandResult CheatUnit(PlayerState p, CheatUnitCommand c) {
            if (!Db.TryGetUnit(c.UnitId, out _)) return CommandError.InvalidUnit;
            if (c.Rank == HeroRank.B) {
                return Roster.AddUnit(ctx, p, c.UnitId, BoardLocked) != null ? CommandResult.Success : CommandError.BenchFull;
            }
            var unit = Roster.Create(ctx, p, c.UnitId, c.Rank, Roster.FirstFreeBenchSlot(p));
            return unit != null ? CommandResult.Success : CommandError.BenchFull;
        }

        CommandResult CheatSetShop(PlayerState p, CheatSetShopCommand c) {
            var slots = p.Shop.Slots;
            var ids = c.UnitIds ?? Array.Empty<string>();
            for (var i = 0; i < slots.Length; i++) {
                var id = i < ids.Length ? ids[i] : null;
                if (!string.IsNullOrEmpty(id) && !Db.TryGetUnit(id, out _)) return CommandError.InvalidUnit;
            }
            for (var i = 0; i < slots.Length; i++) {
                var id = i < ids.Length ? ids[i] : null;
                slots[i] = string.IsNullOrEmpty(id) ? null : id;
            }
            Events.Add(new ShopChangedEvent { PlayerId = p.Id, Rerolled = true, Locked = p.Shop.Locked });
            return CommandResult.Success;
        }

        // ------------------------------------------------------------------ queries for views

        public CommandError CanMove(int playerId, int unitId, UnitLocation target) {
            if (State.Phase != MatchPhase.Preparation) return CommandError.WrongPhase;
            var p = State.Player(playerId);
            return p == null ? CommandError.InvalidPlayer : Roster.CanMove(ctx, p, p.FindUnit(unitId), target);
        }

        public bool CanEquip(int playerId, int unitId, string itemId) {
            var p = State.Player(playerId);
            var unit = p?.FindUnit(unitId);
            if (unit == null || p.ItemCount(itemId) <= 0) return false;
            if (BoardLocked && unit.Location.IsBoard) return false;
            return Items.CanEquip(ctx, unit, itemId);
        }
    }
}
