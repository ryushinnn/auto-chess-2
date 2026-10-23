using System;
using System.Collections.Generic;

namespace AutoChess.Core {
    /// <summary>Services shared by the rule systems of one match.</summary>
    public sealed class MatchContext {
        public readonly GameDatabase Db;
        public readonly GameRules Rules;
        public readonly DeterministicRandom Rng;
        public readonly EventBuffer Events;
        public readonly IUnitPool Pool;
        public MatchState State;

        public MatchContext(GameDatabase db, DeterministicRandom rng, EventBuffer events, IUnitPool pool) {
            Db = db;
            Rules = db.Rules;
            Rng = rng;
            Events = events;
            Pool = pool;
        }

        public int NextUnitId() => ++State.LastUnitId;
    }

    /// <summary>
    /// Where shop rolls come from. The original game draws from an endless pool; TFT-style multiplayer shares
    /// a finite pool between players (bought / displayed units are taken out, sold / rerolled ones go back).
    /// </summary>
    public interface IUnitPool {
        /// <summary>A unit of the given reputation for a shop slot, or null when none is available.</summary>
        string Roll(Reputation reputation, DeterministicRandom rng);
        /// <summary>The unit left a shop without being bought (reroll) or was sold.</summary>
        void Release(string unitId, int copies);
    }

    public sealed class UnlimitedUnitPool : IUnitPool {
        readonly GameDatabase db;

        public UnlimitedUnitPool(GameDatabase db) {
            this.db = db;
        }

        public string Roll(Reputation reputation, DeterministicRandom rng) {
            var pool = db.ShopPool(reputation);
            return pool.Count == 0 ? null : pool[rng.Range(0, pool.Count)].Id;
        }

        public void Release(string unitId, int copies) { }
    }

    /// <summary>
    /// TFT's shared pool: <see cref="GameRules.PoolCopies"/> copies of every unit for the whole match. A shop slot
    /// takes a copy (weighted by the copies left, so contested units dry up), a reroll or a sale puts copies back.
    /// Units granted by rewards and starter packs come from outside the pool; selling them never overfills it.
    /// </summary>
    public sealed class SharedUnitPool : IUnitPool {
        readonly GameDatabase db;
        readonly Dictionary<string, int> index = new(StringComparer.Ordinal);
        readonly int[] max;
        /// <summary>Copies left, by index in <see cref="GameDatabase.Units"/>.</summary>
        public readonly int[] Remaining;

        public SharedUnitPool(GameDatabase db) {
            this.db = db;
            var units = db.Units;
            max = new int[units.Count];
            Remaining = new int[units.Count];
            for (var i = 0; i < units.Count; i++) {
                index[units[i].Id] = i;
                var copies = units[i].Summoned ? 0 : db.Rules.PoolCopies[Math.Clamp((int)units[i].Reputation, 0, db.Rules.PoolCopies.Length - 1)];
                max[i] = Remaining[i] = copies;
            }
        }

        public int CountOf(string unitId) => index.TryGetValue(unitId ?? "", out var i) ? Remaining[i] : 0;

        public string Roll(Reputation reputation, DeterministicRandom rng) {
            var pool = db.ShopPool(reputation);
            var total = 0;
            foreach (var u in pool) total += Remaining[index[u.Id]];
            if (total <= 0) return null;
            var pick = rng.Range(0, total);
            foreach (var u in pool) {
                var i = index[u.Id];
                pick -= Remaining[i];
                if (pick >= 0) continue;
                Remaining[i]--;
                return u.Id;
            }
            return null;
        }

        public void Release(string unitId, int copies) {
            if (unitId == null || !index.TryGetValue(unitId, out var i)) return;
            Remaining[i] = Math.Min(max[i], Remaining[i] + copies);
        }
    }
}
