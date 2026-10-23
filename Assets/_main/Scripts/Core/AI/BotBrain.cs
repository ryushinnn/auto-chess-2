using System;
using System.Collections.Generic;

namespace AutoChess.Core.AI {
    /// <summary>
    /// A simple computer player: picks a starter, buys what it can afford (preferring copies of units it owns,
    /// for the 3-of-a-kind merge), fields its bench, sells loose units when the bench is full, levels up with
    /// spare gold (keeping some for interest) and readies up. Decides from the
    /// state only and returns at most one command per call, so the same brain drives the server's fill-in bots
    /// (state = the authoritative match) and headless client bots (state = their replica).
    /// </summary>
    public sealed class BotBrain {
        public readonly int PlayerId;
        /// <summary>Frames between two decisions (a little jitter is added).</summary>
        public int ThinkFrames = 6;
        /// <summary>Decisions per preparation phase before it readies up regardless.</summary>
        public int MaxActionsPerPhase = 20;
        /// <summary>Gold kept in the bank before spending on rerolls / xp.</summary>
        public int Savings = 10;

        readonly DeterministicRandom rng;
        int wait;
        int actions;
        int phaseKey = -1;

        public BotBrain(int playerId, ulong seed) {
            PlayerId = playerId;
            rng = new DeterministicRandom(seed ^ (0x9E3779B97F4A7C15UL * (ulong)(playerId + 1)));
        }

        /// <summary>
        /// Called once per frame. <paramref name="canMove"/> is the session's move check (unit id, target).
        /// Returns the command to submit now, or null.
        /// </summary>
        public GameCommand Think(MatchState state, GameDatabase db, Func<int, UnitLocation, CommandError> canMove) {
            var p = state.Player(PlayerId);
            if (p == null || p.Eliminated) return null;
            var key = (state.StageIndex * 64 + state.RoundIndex) * 16 + (int)state.Phase;
            if (key != phaseKey) {
                phaseKey = key;
                actions = 0;
                wait = rng.Range(ThinkFrames, ThinkFrames * 3);
            }
            if (--wait > 0) return null;
            wait = ThinkFrames + rng.Range(0, ThinkFrames);

            switch (state.Phase) {
                case MatchPhase.Setup:
                    return p.StarterChosen || actions++ > 0 ? null : new ChooseStarterCommand { OfferIndex = rng.Range(0, p.StarterOffers.Length) };
                case MatchPhase.Preparation:
                    if (p.Ready) return null;
                    return actions++ >= MaxActionsPerPhase ? new ReadyCommand() : Prepare(p, db, canMove) ?? new ReadyCommand();
                default:
                    return null;
            }
        }

        GameCommand Prepare(PlayerState p, GameDatabase db, Func<int, UnitLocation, CommandError> canMove) {
            var rules = db.Rules;

            // Field the bench: first a unit whose move the rules accept.
            foreach (var unit in p.Units) {
                if (!unit.Location.IsBench) continue;
                for (var attempt = 0; attempt < 12; attempt++) {
                    var target = UnitLocation.Board(rng.Range(0, rules.BoardRows), rng.Range(0, rules.BoardColumns));
                    if (canMove(unit.Id, target) == CommandError.None) return new MoveUnitCommand { UnitId = unit.Id, Target = target };
                }
            }

            // Bench full: sell a unit it has no other copy of, to make room.
            if (Roster.FirstFreeBenchSlot(p) < 0 && rng.Chance(0.5)) {
                foreach (var unit in p.Units) {
                    if (!unit.Location.IsBench) continue;
                    var copies = 0;
                    foreach (var other in p.Units) {
                        if (other.DefinitionId == unit.DefinitionId) copies++;
                    }
                    if (copies == 1) return new SellUnitCommand { UnitId = unit.Id };
                }
            }

            // Buy: copies of owned units first, then anything affordable while there is bench room.
            if (Roster.FirstFreeBenchSlot(p) >= 0) {
                var best = -1;
                var bestScore = int.MinValue;
                var owned = new HashSet<string>();
                foreach (var u in p.Units) owned.Add(u.DefinitionId);
                for (var i = 0; i < p.Shop.Slots.Length; i++) {
                    var id = p.Shop.Slots[i];
                    if (id == null || !db.TryGetUnit(id, out var def)) continue;
                    var price = rules.PriceOf(def.Reputation);
                    if (price > p.Gold) continue;
                    var score = (owned.Contains(id) ? 100 : 0) + price * 10 + rng.Range(0, 10);
                    if (score > bestScore) {
                        bestScore = score;
                        best = i;
                    }
                }
                if (best >= 0) return new BuyUnitCommand { ShopSlot = best };
            }

            // Spare gold: level up, or reroll the shop.
            var spare = p.Gold - Savings;
            if (spare >= rules.XpPurchaseCost && !Economy.IsMaxLevel(rules, p) && rng.Chance(0.6)) return new BuyXpCommand();
            if (spare >= rules.RefreshCost && rng.Chance(0.5)) return new RefreshShopCommand();
            return null;
        }
    }
}
