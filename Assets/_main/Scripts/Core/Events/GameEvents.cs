using System.Collections.Generic;

namespace AutoChess.Core {
    /// <summary>
    /// Something the simulation reports to observers (views, the network layer, tests). Events describe what
    /// happened; the authoritative data stays in the state, so a client that missed events can rebuild from a
    /// snapshot. Never mutate state from an event handler - submit a command instead.
    /// </summary>
    public abstract class GameEvent {
        public int Tick;
    }

    public sealed class EventBuffer {
        readonly List<GameEvent> events = new();
        public int Tick;

        public IReadOnlyList<GameEvent> Pending => events;
        public int Count => events.Count;

        public T Add<T>(T e) where T : GameEvent {
            e.Tick = Tick;
            events.Add(e);
            return e;
        }

        /// <summary>Moves every pending event into <paramref name="target"/> and clears the buffer.</summary>
        public void DrainTo(List<GameEvent> target) {
            target.AddRange(events);
            events.Clear();
        }

        public void Clear() => events.Clear();
    }

    // ------------------------------------------------------------------ match / player events

    public sealed class PhaseChangedEvent : GameEvent {
        public MatchPhase Phase;
        public MatchPhase Previous;
        public int DurationTicks;
        public int Stage;
        public int Round;
    }

    public sealed class MatchFinishedEvent : GameEvent { }

    public abstract class PlayerEvent : GameEvent {
        public int PlayerId;
    }

    public sealed class GoldChangedEvent : PlayerEvent {
        public int Gold;
        public int Delta;
    }

    public sealed class XpChangedEvent : PlayerEvent {
        public int Level;
        public int Xp;
        /// <summary>-1 at the max level.</summary>
        public int XpToNext;
        public bool LeveledUp;
    }

    public sealed class BoardLimitChangedEvent : PlayerEvent {
        public int Count;
        public int Max;
    }

    public sealed class ShopChangedEvent : PlayerEvent {
        /// <summary>True when the whole shop was re-rolled (play the reveal animation).</summary>
        public bool Rerolled;
        public bool Locked;
    }

    public sealed class StarterOffersChangedEvent : PlayerEvent {
        /// <summary>-1 when every offer changed.</summary>
        public int OfferIndex;
    }

    public sealed class UnitAddedEvent : PlayerEvent {
        public int UnitId;
    }

    public sealed class UnitRemovedEvent : PlayerEvent {
        public int UnitId;
        /// <summary>Unit the removed one was merged into, or -1.</summary>
        public int MergedInto = -1;
    }

    public sealed class UnitMovedEvent : PlayerEvent {
        public int UnitId;
        public UnitLocation From;
        public UnitLocation To;
    }

    public sealed class UnitRankChangedEvent : PlayerEvent {
        public int UnitId;
        public HeroRank Rank;
    }

    public sealed class UnitItemsChangedEvent : PlayerEvent {
        public int UnitId;
    }

    public sealed class ItemBenchChangedEvent : PlayerEvent {
        /// <summary>Item that was just received, or null.</summary>
        public string AddedItemId;
    }

    public sealed class TraitsChangedEvent : PlayerEvent { }

    public sealed class BattleCreatedEvent : PlayerEvent {
        public int BattleId;
    }

    public sealed class BattleFinishedEvent : PlayerEvent {
        public int BattleId;
        public BattleOutcome Outcome;
    }

    public sealed class RewardsGrantedEvent : PlayerEvent {
        public int Gold;
        public int Units;
        public int Items;
    }
}
