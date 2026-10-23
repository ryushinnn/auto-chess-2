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
}
