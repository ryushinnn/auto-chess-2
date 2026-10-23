using System.Collections.Generic;

namespace AutoChess.Core {
    /// <summary>
    /// Owned units: adding (with 3-of-a-kind merges), moving between bench and board, auto-filling the board
    /// before a battle (was LineUp + HeroPicker's rules).
    /// </summary>
    public static class Roster {
        /// <summary>
        /// Gives the player a copy of <paramref name="definitionId"/>. Two copies of the same rank already owned
        /// merge with it into the next rank (and three of those into the one after). With
        /// <paramref name="boardLocked"/> (outside preparation) only bench units take part in merges; the rest
        /// merge when the next preparation starts. Returns the unit holding the copy, or null when it does not
        /// fit on the bench.
        /// </summary>
        public static UnitInstance AddUnit(MatchContext ctx, PlayerState p, string definitionId, bool boardLocked) {
            var needed = ctx.Rules.UnitsToRankUp;
            var merged = Merge(ctx, p, definitionId, HeroRank.B, needed - 1, boardLocked);
            if (merged != null) {
                return Merge(ctx, p, definitionId, HeroRank.A, needed, boardLocked) ?? merged;
            }

            var slot = FirstFreeBenchSlot(p);
            return slot < 0 ? null : Create(ctx, p, definitionId, HeroRank.B, slot);
        }

        /// <summary>Creates a unit on a free bench slot without any merge (starter pack, cheats).</summary>
        public static UnitInstance Create(MatchContext ctx, PlayerState p, string definitionId, HeroRank rank, int slot) {
            if (slot < 0 || slot >= p.Bench.Length || p.Bench[slot] != null) return null;
            var unit = new UnitInstance(ctx.NextUnitId(), definitionId, rank);
            p.Units.Add(unit);
            Place(p, unit, UnitLocation.Bench(slot));
            ctx.Events.Add(new UnitAddedEvent { PlayerId = p.Id, UnitId = unit.Id });
            return unit;
        }

        /// <summary>
        /// Merges <paramref name="required"/> owned copies of a rank into one of the next rank. The survivor is
        /// the one on the board (lowest cell) or else the lowest bench slot; the others' items move onto it.
        /// </summary>
        static UnitInstance Merge(MatchContext ctx, PlayerState p, string definitionId, HeroRank rank, int required,
            bool boardLocked) {
            if (rank == HeroRank.S || required <= 0) return null;
            var candidates = new List<UnitInstance>(required);
            foreach (var u in p.Units) {
                if (u.DefinitionId != definitionId || u.Rank != rank) continue;
                if (boardLocked && !u.Location.IsBench) continue;
                candidates.Add(u);
            }
            if (candidates.Count < required) return null;

            candidates.Sort(CompareKeepPriority);
            var keep = candidates[0];
            for (var i = 1; i < required; i++) {
                var other = candidates[i];
                Remove(ctx, p, other, keep.Id);
                foreach (var item in other.Items) {
                    if (!Items.TryEquip(ctx, keep, item)) Items.AddToBench(ctx, p, item);
                }
            }
            keep.Rank = rank.Next();
            ctx.Events.Add(new UnitRankChangedEvent { PlayerId = p.Id, UnitId = keep.Id, Rank = keep.Rank });
            ctx.Events.Add(new UnitItemsChangedEvent { PlayerId = p.Id, UnitId = keep.Id });
            if (keep.Location.IsBoard) BoardChanged(ctx, p);
            return keep;
        }

        static int CompareKeepPriority(UnitInstance a, UnitInstance b) {
            var aBoard = a.Location.IsBoard;
            var bBoard = b.Location.IsBoard;
            if (aBoard != bBoard) return aBoard ? -1 : 1;
            if (aBoard) {
                var c = a.Location.Cell.Row.CompareTo(b.Location.Cell.Row);
                return c != 0 ? c : a.Location.Cell.Col.CompareTo(b.Location.Cell.Col);
            }
            return a.Location.Slot.CompareTo(b.Location.Slot);
        }

        /// <summary>Merges every complete set left over from purchases made while the board was locked.</summary>
        public static void ResolveMerges(MatchContext ctx, PlayerState p) {
            var needed = ctx.Rules.UnitsToRankUp;
            bool changed;
            do {
                changed = false;
                foreach (var u in p.Units) {
                    if (u.Rank == HeroRank.S) continue;
                    if (Merge(ctx, p, u.DefinitionId, u.Rank, needed, false) != null) {
                        changed = true;
                        break; // p.Units changed
                    }
                }
            } while (changed);
        }

        public static void Remove(MatchContext ctx, PlayerState p, UnitInstance unit, int mergedInto = -1) {
            var wasOnBoard = unit.Location.IsBoard;
            Vacate(p, unit);
            p.Units.Remove(unit);
            ctx.Events.Add(new UnitRemovedEvent { PlayerId = p.Id, UnitId = unit.Id, MergedInto = mergedInto });
            if (wasOnBoard) BoardChanged(ctx, p);
        }

        /// <summary>Copies a unit of this rank is made of (1, 3, 9).</summary>
        public static int Copies(GameRules rules, HeroRank rank) {
            var n = 1;
            for (var i = 0; i < (int)rank; i++) n *= rules.UnitsToRankUp;
            return n;
        }

        /// <summary>TFT: full price for a 1-star (or any 1-gold) unit, one gold less for higher ranks.</summary>
        public static int SellValue(GameDatabase db, UnitInstance unit) {
            var price = db.Rules.PriceOf(db.Unit(unit.DefinitionId).Reputation);
            var value = price * Copies(db.Rules, unit.Rank);
            return unit.Rank > HeroRank.B && price > 1 ? value - 1 : value;
        }

        public static CommandResult Sell(MatchContext ctx, PlayerState p, UnitInstance unit, bool boardLocked) {
            if (unit == null) return CommandError.InvalidUnit;
            if (unit.Location.IsBoard && boardLocked) return CommandError.WrongPhase;
            var gold = SellValue(ctx.Db, unit);
            var items = unit.Items.ToArray();
            unit.Items.Clear();
            foreach (var item in items) Items.AddToBench(ctx, p, item);
            Remove(ctx, p, unit);
            ctx.Pool.Release(unit.DefinitionId, Copies(ctx.Rules, unit.Rank));
            Economy.GainGold(ctx, p, gold);
            ctx.Events.Add(new UnitSoldEvent { PlayerId = p.Id, UnitId = unit.Id, DefinitionId = unit.DefinitionId, Gold = gold });
            return CommandResult.Success;
        }

        public static int FirstFreeBenchSlot(PlayerState p) {
            for (var i = 0; i < p.Bench.Length; i++) {
                if (p.Bench[i] == null) return i;
            }
            return -1;
        }

        public static UnitInstance At(MatchContext ctx, PlayerState p, UnitLocation location) {
            switch (location.Kind) {
                case LocationKind.Bench:
                    return location.Slot >= 0 && location.Slot < p.Bench.Length ? p.Bench[location.Slot] : null;
                case LocationKind.Board:
                    return ctx.Db.Grid.Contains(location.Cell) ? p.Board[ctx.Db.Grid.IndexOf(location.Cell)] : null;
                default:
                    return null;
            }
        }

        public static bool IsValidTarget(MatchContext ctx, PlayerState p, UnitLocation location) {
            switch (location.Kind) {
                case LocationKind.Bench:
                    return location.Slot >= 0 && location.Slot < p.Bench.Length;
                case LocationKind.Board:
                    return ctx.Db.Grid.Contains(location.Cell) && location.Cell.Row < ctx.Rules.PlayerRows;
                default:
                    return false;
            }
        }

        /// <summary>Would moving <paramref name="unit"/> to <paramref name="target"/> be accepted? (drag preview)</summary>
        public static CommandError CanMove(MatchContext ctx, PlayerState p, UnitInstance unit, UnitLocation target) {
            if (unit == null || !p.Units.Contains(unit)) return CommandError.InvalidUnit;
            if (!IsValidTarget(ctx, p, target)) return CommandError.InvalidLocation;
            if (target == unit.Location) return CommandError.None;
            var occupant = At(ctx, p, target);
            if (occupant != null) return CommandError.None; // swap keeps the board count
            if (target.IsBoard && !unit.Location.IsBoard && p.BoardCount >= Economy.MaxBoardUnits(ctx.Rules, p)) {
                return CommandError.BoardFull;
            }
            return CommandError.None;
        }

        /// <summary>Moves a unit to an empty spot, or swaps it with the unit standing there.</summary>
        public static CommandResult Move(MatchContext ctx, PlayerState p, UnitInstance unit, UnitLocation target) {
            var error = CanMove(ctx, p, unit, target);
            if (error != CommandError.None) return error;
            if (target == unit.Location) return CommandResult.Success;

            var from = unit.Location;
            var occupant = At(ctx, p, target);
            var boardBefore = from.IsBoard || target.IsBoard;
            Vacate(p, unit);
            if (occupant != null) {
                Vacate(p, occupant);
                Place(p, occupant, from);
            }
            Place(p, unit, target);

            ctx.Events.Add(new UnitMovedEvent { PlayerId = p.Id, UnitId = unit.Id, From = from, To = target });
            if (occupant != null) {
                ctx.Events.Add(new UnitMovedEvent { PlayerId = p.Id, UnitId = occupant.Id, From = target, To = from });
            }
            if (boardBefore) BoardChanged(ctx, p);
            return CommandResult.Success;
        }

        /// <summary>Before a battle, bench units fill the free board slots (lowest cells first).</summary>
        public static void AutoFillBoard(MatchContext ctx, PlayerState p) {
            var max = Economy.MaxBoardUnits(ctx.Rules, p);
            var grid = ctx.Db.Grid;
            var changed = false;
            for (var slot = 0; slot < p.Bench.Length && p.BoardCount < max; slot++) {
                var unit = p.Bench[slot];
                if (unit == null) continue;
                var cell = FirstFreeCell(ctx, p);
                if (cell < 0) break;
                var from = unit.Location;
                Vacate(p, unit);
                Place(p, unit, UnitLocation.Board(grid.CoordOf(cell)));
                ctx.Events.Add(new UnitMovedEvent { PlayerId = p.Id, UnitId = unit.Id, From = from, To = unit.Location });
                changed = true;
            }
            if (changed) BoardChanged(ctx, p);
        }

        static int FirstFreeCell(MatchContext ctx, PlayerState p) {
            var grid = ctx.Db.Grid;
            for (var r = 0; r < ctx.Rules.PlayerRows; r++) {
                for (var c = 0; c < grid.Cols; c++) {
                    var i = grid.IndexOf(new HexCoord(r, c));
                    if (p.Board[i] == null) return i;
                }
            }
            return -1;
        }

        public static void BoardChanged(MatchContext ctx, PlayerState p) {
            ctx.Events.Add(new BoardLimitChangedEvent {
                PlayerId = p.Id, Count = p.BoardCount, Max = Economy.MaxBoardUnits(ctx.Rules, p)
            });
            ctx.Events.Add(new TraitsChangedEvent { PlayerId = p.Id });
        }

        static void Place(PlayerState p, UnitInstance unit, UnitLocation location) {
            unit.Location = location;
            if (location.IsBench) p.Bench[location.Slot] = unit;
            else if (location.IsBoard) p.Board[p.BoardIndex(location.Cell)] = unit;
        }

        static void Vacate(PlayerState p, UnitInstance unit) {
            var location = unit.Location;
            if (location.IsBench && p.Bench[location.Slot] == unit) p.Bench[location.Slot] = null;
            else if (location.IsBoard && p.Board[p.BoardIndex(location.Cell)] == unit) p.Board[p.BoardIndex(location.Cell)] = null;
            unit.Location = UnitLocation.None;
        }
    }
}
