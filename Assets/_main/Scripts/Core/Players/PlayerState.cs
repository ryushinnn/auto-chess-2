using System;
using System.Collections.Generic;

namespace AutoChess.Core {
    public enum LocationKind : byte {
        None,
        Bench,
        Board,
    }

    /// <summary>Where an owned unit stands: a bench slot or a board cell of its owner's half.</summary>
    [Serializable]
    public readonly struct UnitLocation : IEquatable<UnitLocation> {
        public readonly LocationKind Kind;
        public readonly int Slot;
        public readonly HexCoord Cell;

        UnitLocation(LocationKind kind, int slot, HexCoord cell) {
            Kind = kind;
            Slot = slot;
            Cell = cell;
        }

        public static readonly UnitLocation None = default;
        public static UnitLocation Bench(int slot) => new(LocationKind.Bench, slot, default);
        public static UnitLocation Board(HexCoord cell) => new(LocationKind.Board, 0, cell);
        public static UnitLocation Board(int row, int col) => new(LocationKind.Board, 0, new HexCoord(row, col));

        public bool IsBench => Kind == LocationKind.Bench;
        public bool IsBoard => Kind == LocationKind.Board;

        public bool Equals(UnitLocation other) => Kind == other.Kind && Slot == other.Slot && Cell == other.Cell;
        public override bool Equals(object obj) => obj is UnitLocation other && Equals(other);
        public override int GetHashCode() => ((int)Kind << 24) ^ (Slot << 16) ^ Cell.GetHashCode();
        public static bool operator ==(UnitLocation a, UnitLocation b) => a.Equals(b);
        public static bool operator !=(UnitLocation a, UnitLocation b) => !a.Equals(b);

        public override string ToString() => Kind switch {
            LocationKind.Bench => $"bench:{Slot}",
            LocationKind.Board => $"board:{Cell}",
            _ => "none"
        };

        /// <summary>Parses "bench:3" or "board:1,4".</summary>
        public static bool TryParse(string text, out UnitLocation location) {
            location = None;
            if (string.IsNullOrEmpty(text)) return false;
            var colon = text.IndexOf(':');
            if (colon < 0) return false;
            var kind = text.Substring(0, colon).Trim().ToLowerInvariant();
            var rest = text.Substring(colon + 1);
            if (kind == "bench" && int.TryParse(rest, out var slot)) {
                location = Bench(slot);
                return true;
            }
            if (kind == "board" && HexCoord.TryParse(rest, out var cell)) {
                location = Board(cell);
                return true;
            }
            return false;
        }
    }

    /// <summary>A unit owned by a player (on the bench or on the board), outside of battle.</summary>
    public sealed class UnitInstance {
        public readonly int Id;
        public readonly string DefinitionId;
        public HeroRank Rank;
        public UnitLocation Location;
        /// <summary>Equipped items, combined items first (the order the old HeroInventory kept).</summary>
        public readonly List<string> Items = new();

        public UnitInstance(int id, string definitionId, HeroRank rank) {
            Id = id;
            DefinitionId = definitionId;
            Rank = rank;
        }

        public override string ToString() => $"#{Id} {DefinitionId}({Rank}) @{Location}";
    }

    public sealed class ShopState {
        /// <summary>Offered unit ids; null = empty (bought) slot.</summary>
        public readonly string[] Slots;
        public bool Locked;
        /// <summary>Bad-luck protection: grows by the legendary odds on every non-legendary roll.</summary>
        public int Insurance;

        public ShopState(int slots) {
            Slots = new string[slots];
        }
    }

    public sealed class ItemStack {
        public readonly string ItemId;
        public int Count;

        public ItemStack(string itemId, int count) {
            ItemId = itemId;
            Count = count;
        }
    }

    public sealed class StarterOffer {
        public string UnitId;
        public string ItemId;
    }

    public sealed class PlayerState {
        public readonly int Id;
        public string Name;
        public int Gold;
        public int Level = 1;
        public int Xp;
        public bool Ready;
        public bool StarterChosen;
        /// <summary>This player's own random stream (shop, rewards, starter pack): one player's rerolls never change another's luck.</summary>
        public DeterministicRandom Rng = new(1);

        /// <summary>Every owned unit, in creation order.</summary>
        public readonly List<UnitInstance> Units = new();
        public readonly UnitInstance[] Bench;
        /// <summary>Indexed by HexGrid.IndexOf; only the player's half is ever used.</summary>
        public readonly UnitInstance[] Board;
        /// <summary>Item bench (unequipped items), in the order they were first received.</summary>
        public readonly List<ItemStack> Items = new();
        public readonly ShopState Shop;
        public readonly StarterOffer[] StarterOffers;

        /// <summary>Rewards earned by the last battle, granted when the next preparation starts.</summary>
        public readonly List<RewardDefinition> PendingRewards = new();
        public BattleOutcome LastOutcome;
        public int Wins;
        public int Losses;

        readonly int boardCols;

        public PlayerState(int id, GameRules rules) {
            Id = id;
            boardCols = rules.BoardColumns;
            Bench = new UnitInstance[rules.BenchSize];
            Board = new UnitInstance[rules.BoardRows * rules.BoardColumns];
            Shop = new ShopState(rules.ShopSlots);
            StarterOffers = new StarterOffer[rules.StarterOfferCount];
            for (var i = 0; i < StarterOffers.Length; i++) StarterOffers[i] = new StarterOffer();
            Gold = rules.StartingGold;
        }

        public int BoardCount {
            get {
                var n = 0;
                foreach (var u in Units) {
                    if (u.Location.IsBoard) n++;
                }
                return n;
            }
        }

        public int BoardIndex(HexCoord cell) => cell.Row * boardCols + cell.Col;

        public UnitInstance FindUnit(int unitId) {
            foreach (var u in Units) {
                if (u.Id == unitId) return u;
            }
            return null;
        }

        public int ItemCount(string itemId) {
            foreach (var s in Items) {
                if (s.ItemId == itemId) return s.Count;
            }
            return 0;
        }

        public override string ToString() => $"P{Id} gold={Gold} lv={Level} units={Units.Count}";
    }
}
