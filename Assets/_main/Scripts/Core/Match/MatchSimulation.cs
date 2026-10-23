using System;
using System.Collections.Generic;
using AutoChess.Core.Combat;

namespace AutoChess.Core {
    /// <summary>
    /// The authoritative game: owns the <see cref="MatchState"/>, advances it in fixed ticks and applies
    /// player commands (was GameManager + Progress + the feature MonoBehaviours). Engine-free and
    /// deterministic: the same seed and the same commands at the same ticks give the same match, which is
    /// what a dedicated server, a lockstep client, replays and the regression tests need.
    ///
    /// Phase loop (durations in <see cref="GameRules"/>):
    /// Setup (starter pick) -> Preparation -> Transition (boards auto-filled, battles built: PvE vs the round's
    /// creeps, or PvP pairs + a ghost when the count is odd) -> Battle (until every battle ends or time runs out)
    /// -> Summary (losers take damage, eliminations, rewards decided) -> Preparation (+xp, income, rewards
    /// granted, merges resolved, shop refreshed) ... -> Finished when one player is left (or the rounds run out).
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
        public double PhaseTimeLeft => State.PhaseTicksLeft * Rules.TickDelta;
        public double PhaseDuration => State.PhaseTicksTotal * Rules.TickDelta;
        public bool BoardLocked => State.Phase != MatchPhase.Preparation && State.Phase != MatchPhase.Setup;

        public MatchSimulation(GameDatabase db, MatchConfig config) {
            Db = db ?? throw new ArgumentNullException(nameof(db));
            Config = config ?? new MatchConfig();
            rng = new DeterministicRandom(Config.Seed);
            abilities = Config.Abilities ?? AbilityRegistry.Default;
            var count = Math.Max(1, Config.PlayerCount);
            var pool = Config.Pool;
            if (pool == null && count > 1) pool = State.SharedPool = new SharedUnitPool(db);
            ctx = new MatchContext(db, rng, Events, pool ?? new UnlimitedUnitPool(db)) { State = State };

            for (var i = 0; i < count; i++) {
                var p = new PlayerState(i, db.Rules) {
                    Name = Config.PlayerNames != null && i < Config.PlayerNames.Length ? Config.PlayerNames[i] : $"Player {i + 1}",
                    Rng = rng.Fork((uint)(i + 1)),
                };
                State.Players.Add(p);
            }
        }

        public PlayerState Player(int id) => State.Player(id);

        /// <summary>
        /// Two or more players: the TFT game (income, interest, streaks, a shared unit pool, PvP stages after the
        /// authored ones, last player standing wins). One player: the original PvE campaign of the authored stages.
        /// </summary>
        public bool IsMultiplayer => State.Players.Count > 1;

        /// <summary>A round of the match: its kind and, for PvE, the creeps (scaled for generated stages).</summary>
        public readonly struct RoundInfo {
            public readonly bool Exists;
            public readonly RoundKind Kind;
            public readonly RoundDefinition Pve;
            public readonly double Scale;

            public RoundInfo(RoundKind kind, RoundDefinition pve, double scale) {
                Exists = true;
                Kind = kind;
                Pve = pve;
                Scale = scale;
            }
        }

        public RoundInfo CurrentRoundInfo => RoundAt(State.StageIndex, State.RoundIndex);
        /// <summary>Creeps of the current round (null in a PvP round or after the last round).</summary>
        public RoundDefinition CurrentRound => CurrentRoundInfo.Pve;

        int AuthoredStages => Db.Stages.Count;

        int RoundCount(int stage) => stage < AuthoredStages ? Db.Stages[stage].Rounds.Length : Rules.RoundsPerStage;

        public RoundInfo RoundAt(int stage, int round) {
            if (stage < AuthoredStages) {
                var rounds = Db.Stages[stage].Rounds;
                return round < rounds.Length ? new RoundInfo(RoundKind.PvE, rounds[round], 1) : default;
            }
            if (!IsMultiplayer || stage >= Rules.MaxStages || round >= Rules.RoundsPerStage) return default;
            if (round == Rules.PveRoundOfStage - 1) {
                var authored = 0;
                foreach (var s in Db.Stages) authored += s.Rounds.Length;
                if (authored > 0) {
                    var generated = stage - AuthoredStages;
                    var pick = generated % authored;
                    foreach (var s in Db.Stages) {
                        if (pick < s.Rounds.Length) return new RoundInfo(RoundKind.PvE, s.Rounds[pick], Math.Pow(Rules.PveScalingPerStage, generated + 1));
                        pick -= s.Rounds.Length;
                    }
                }
            }
            return new RoundInfo(RoundKind.PvP, null, 1);
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
                SetPhase(MatchPhase.Setup, Config.SetupSeconds ?? Rules.SetupSeconds);
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
                foreach (var battle in State.BattleList) {
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
                    foreach (var battle in State.BattleList) battle.ForceEnd(BattleOutcome.Draw);
                    EnterSummary();
                    break;
                case MatchPhase.Summary:
                    NextRound();
                    break;
            }
        }

        void SetPhase(MatchPhase phase, double seconds) {
            var previous = State.Phase;
            State.Phase = phase;
            State.PhaseTicksTotal = State.PhaseTicksLeft = Math.Max(1, Rules.SecondsToTicks(seconds));
            Events.Add(new PhaseChangedEvent {
                Phase = phase, Previous = previous, DurationTicks = State.PhaseTicksTotal,
                Stage = State.StageIndex, Round = State.RoundIndex
            });
        }

        void EnterPreparation(bool first) {
            var info = CurrentRoundInfo;
            if (!info.Exists) {
                Finish();
                return;
            }
            State.RoundKind = info.Kind;
            if (!first) {
                foreach (var p in State.Players) {
                    if (p.Eliminated) continue;
                    Economy.GainXp(ctx, p, Rules.XpPerRound);
                    if (IsMultiplayer) PayIncome(p);
                }
            }
            State.Battles.Clear();
            State.BattleList.Clear();
            SetPhase(MatchPhase.Preparation, Config.PreparationSeconds ?? Rules.PreparationSeconds);
            foreach (var p in State.Players) {
                if (p.Eliminated) continue;
                p.Ready = false;
                Rewards.GrantPending(ctx, p);
                Roster.ResolveMerges(ctx, p);
                Shop.AutoRefresh(ctx, p);
            }
        }

        /// <summary>Round income (TFT): base by round + 1 per 10 gold banked (max 5) + win / loss streak bonus.</summary>
        void PayIncome(PlayerState p) {
            var income = new IncomeEvent {
                PlayerId = p.Id,
                Base = Rules.IncomeOfRound(State.RoundNumber),
                Interest = Rules.InterestStep > 0 ? Math.Min(Rules.MaxInterest, Math.Max(0, p.Gold) / Rules.InterestStep) : 0,
                Streak = p.Streak != 0 ? Rules.StreakBonus(p.Streak) : 0,
            };
            Economy.GainGold(ctx, p, income.Total);
            Events.Add(income);
        }

        void EnterTransition() {
            SetPhase(MatchPhase.Transition, Rules.TransitionSeconds);
            var info = CurrentRoundInfo;
            var alive = new List<PlayerState>();
            foreach (var p in State.Players) {
                if (p.Eliminated) continue;
                alive.Add(p);
                Roster.AutoFillBoard(ctx, p);
            }

            if (info.Kind == RoundKind.PvE || alive.Count < 2) {
                foreach (var p in alive) {
                    var away = info.Pve != null ? BattleSetups.FromRound(info.Pve) : new TeamSetup();
                    if (info.Scale != 1) {
                        foreach (var u in away.Units) {
                            u.HpScale *= info.Scale;
                            u.DamageScale *= info.Scale;
                        }
                    }
                    CreateBattle(BattleSetups.FromBoard(p), away, p, null, false);
                }
                return;
            }

            foreach (var (home, visitor, ghost) in Pairing.Pair(alive, rng)) {
                var away = BattleSetups.FromBoardMirrored(visitor, Rules);
                away.Ghost = ghost;
                CreateBattle(BattleSetups.FromBoard(home), away, home, ghost ? null : visitor, ghost);
            }
        }

        void CreateBattle(TeamSetup home, TeamSetup away, PlayerState host, PlayerState visitor, bool ghost) {
            var setup = new BattleSetup { Seed = ((ulong)rng.NextUInt() << 32) | rng.NextUInt(), Home = home, Away = away };
            var battle = new BattleSimulation(++State.LastBattleId, Db, setup, Events, abilities);
            State.BattleList.Add(battle);
            State.Battles[host.Id] = battle;
            host.OpponentId = away.PlayerId;
            host.OpponentIsGhost = ghost;
            Events.Add(new BattleCreatedEvent { PlayerId = host.Id, BattleId = battle.Id, OpponentId = away.PlayerId, OpponentIsGhost = ghost });
            if (visitor == null) return;
            State.Battles[visitor.Id] = battle;
            visitor.OpponentId = host.Id;
            visitor.OpponentIsGhost = false;
            Events.Add(new BattleCreatedEvent { PlayerId = visitor.Id, BattleId = battle.Id, OpponentId = host.Id, Visiting = true });
        }

        void EnterBattle() {
            SetPhase(MatchPhase.Battle, Rules.BattleSeconds);
            var allDone = true;
            foreach (var battle in State.BattleList) {
                battle.Start();
                allDone &= battle.Finished;
            }
            if (allDone) EnterSummary();
        }

        void EnterSummary() {
            SetPhase(MatchPhase.Summary, Rules.SummarySeconds);
            var info = CurrentRoundInfo;
            foreach (var p in State.Players) p.LastDamageTaken = 0;
            foreach (var battle in State.BattleList) {
                var outcome = battle.Outcome == BattleOutcome.None ? BattleOutcome.Draw : battle.Outcome;
                var homeTeam = battle.Setup.Home;
                var awayTeam = battle.Setup.Away;
                var home = State.Player(homeTeam.PlayerId);
                var away = awayTeam.Ghost ? null : State.Player(awayTeam.PlayerId);
                if (home != null) Conclude(home, battle, outcome == BattleOutcome.HomeWin, outcome, TeamSide.Away, awayTeam.PlayerId, info);
                if (away != null) Conclude(away, battle, outcome == BattleOutcome.AwayWin, outcome, TeamSide.Home, homeTeam.PlayerId, info);
            }
            Eliminate();
        }

        void Conclude(PlayerState p, BattleSimulation battle, bool won, BattleOutcome outcome, TeamSide enemy, int enemyId, RoundInfo info) {
            p.LastOutcome = won ? BattleOutcome.HomeWin : outcome == BattleOutcome.Draw ? BattleOutcome.Draw : BattleOutcome.AwayWin;
            if (won) p.Wins++;
            else p.Losses++;

            if (info.Kind == RoundKind.PvP) {
                p.Streak = won ? Math.Max(1, p.Streak + 1) : Math.Min(-1, p.Streak - 1);
                if (won && IsMultiplayer) Economy.GainGold(ctx, p, Rules.PvpWinGold);
            }
            if (!won) {
                var survivors = 0;
                foreach (var u in battle.Units) {
                    if (u.Alive && u.Side == enemy && !u.Def.Summoned) survivors++;
                }
                var damage = Rules.DamageOfStage(State.StageIndex) + survivors * Rules.DamagePerSurvivingUnit;
                if (damage > 0) {
                    p.Health -= damage;
                    p.LastDamageTaken = damage;
                    Events.Add(new PlayerDamagedEvent { PlayerId = p.Id, Amount = damage, Health = p.Health, SourceId = enemyId });
                }
            }

            p.PendingRewards.Clear();
            if (info.Pve != null) {
                p.PendingRewards.AddRange(info.Pve.Rewards);
                if (won) p.PendingRewards.AddRange(info.Pve.WinRewards);
            }
            Events.Add(new BattleFinishedEvent { PlayerId = p.Id, BattleId = battle.Id, Outcome = p.LastOutcome });
        }

        /// <summary>Players at 0 health or less leave the match; the lowest health places last.</summary>
        void Eliminate() {
            var dead = new List<PlayerState>();
            foreach (var p in State.Players) {
                if (!p.Eliminated && p.Health <= 0) dead.Add(p);
            }
            if (dead.Count == 0) return;
            dead.Sort((a, b) => a.Health != b.Health ? a.Health.CompareTo(b.Health) : b.Id.CompareTo(a.Id));
            var place = State.AliveCount;
            foreach (var p in dead) {
                p.Eliminated = true;
                p.Placement = place--;
                p.Ready = true;
                foreach (var u in p.Units) ctx.Pool.Release(u.DefinitionId, Roster.Copies(Rules, u.Rank));
                for (var i = 0; i < p.Shop.Slots.Length; i++) {
                    if (p.Shop.Slots[i] != null) ctx.Pool.Release(p.Shop.Slots[i], 1);
                    p.Shop.Slots[i] = null;
                }
                Events.Add(new PlayerEliminatedEvent { PlayerId = p.Id, Placement = p.Placement });
            }
        }

        /// <summary>The match is decided: one player left standing (multiplayer), or the only player is out.</summary>
        public bool IsDecided => IsMultiplayer ? State.AliveCount <= 1 : State.AliveCount == 0;

        void NextRound() {
            if (IsDecided) {
                Finish();
                return;
            }
            State.RoundIndex++;
            State.RoundNumber++;
            if (State.RoundIndex >= RoundCount(State.StageIndex)) {
                State.RoundIndex = 0;
                State.StageIndex++;
            }
            EnterPreparation(false);
        }

        void Finish() {
            State.Battles.Clear();
            State.BattleList.Clear();
            // Whoever is still standing takes the top places by health (one winner unless the rounds ran out).
            var standing = new List<PlayerState>();
            foreach (var p in State.Players) {
                if (!p.Eliminated) standing.Add(p);
            }
            standing.Sort((a, b) => a.Health != b.Health ? b.Health.CompareTo(a.Health) : a.Id.CompareTo(b.Id));
            for (var i = 0; i < standing.Count; i++) standing[i].Placement = i + 1;
            SetPhase(MatchPhase.Finished, 0);
            Events.Add(new MatchFinishedEvent { WinnerId = IsMultiplayer && standing.Count > 0 ? standing[0].Id : -1 });
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
            if (p.Eliminated) return CommandError.Eliminated;
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

                case SellUnitCommand c:
                    if (!inGame) return CommandError.WrongPhase;
                    return Roster.Sell(ctx, p, p.FindUnit(c.UnitId), BoardLocked);

                case ReadyCommand _:
                    if (phase != MatchPhase.Preparation) return CommandError.WrongPhase;
                    p.Ready = true;
                    if (State.Players.TrueForAll(x => x.Ready || x.Eliminated)) ShortenPhase();
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
