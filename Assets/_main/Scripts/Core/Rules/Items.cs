namespace AutoChess.Core {
    /// <summary>
    /// Item bench and equipping (was Inventory + HeroInventory). A unit holds up to ItemCapacity items; a
    /// component dropped on a unit whose last item is a component combines with it into the forged item.
    /// </summary>
    public static class Items {
        public static void AddToBench(MatchContext ctx, PlayerState p, string itemId, int count = 1) {
            if (count <= 0) return;
            ctx.Db.Item(itemId); // validates
            var stack = Find(p, itemId);
            if (stack != null) stack.Count += count;
            else p.Items.Add(new ItemStack(itemId, count));
            ctx.Events.Add(new ItemBenchChangedEvent { PlayerId = p.Id, AddedItemId = itemId });
        }

        static ItemStack Find(PlayerState p, string itemId) {
            foreach (var s in p.Items) {
                if (s.ItemId == itemId) return s;
            }
            return null;
        }

        public static bool CanEquip(MatchContext ctx, UnitInstance unit, string itemId) {
            if (!ctx.Db.TryGetItem(itemId, out var item)) return false;
            var items = unit.Items;
            if (items.Count < ctx.Rules.ItemCapacity) return true;
            // Full: only a component landing on a component still fits (they combine).
            return !item.IsCombined && !ctx.Db.Item(items[items.Count - 1]).IsCombined
                   && ctx.Db.Combine(itemId, items[items.Count - 1]) != null;
        }

        /// <summary>Puts an item on a unit, combining two components. False when it does not fit.</summary>
        public static bool TryEquip(MatchContext ctx, UnitInstance unit, string itemId) {
            if (!CanEquip(ctx, unit, itemId)) return false;
            var db = ctx.Db;
            var item = db.Item(itemId);
            var items = unit.Items;

            var last = items.Count > 0 ? db.Item(items[items.Count - 1]) : null;
            var combined = !item.IsCombined && last != null && !last.IsCombined ? db.Combine(itemId, last.Id) : null;
            if (combined != null) {
                items.RemoveAt(items.Count - 1);
                items.Add(combined.Id);
            }
            else {
                if (items.Count >= ctx.Rules.ItemCapacity) return false;
                items.Add(itemId);
            }
            SortCombinedFirst(ctx, unit);
            return true;
        }

        /// <summary>Stable sort: combined items first, then components, each in arrival order.</summary>
        static void SortCombinedFirst(MatchContext ctx, UnitInstance unit) {
            var items = unit.Items;
            for (var i = 1; i < items.Count; i++) {
                var current = items[i];
                if (!ctx.Db.Item(current).IsCombined) continue;
                var j = i - 1;
                while (j >= 0 && !ctx.Db.Item(items[j]).IsCombined) {
                    items[j + 1] = items[j];
                    j--;
                }
                items[j + 1] = current;
            }
        }

        /// <summary>Equips an item from the player's item bench. Board units only accept items in preparation.</summary>
        public static CommandResult EquipFromBench(MatchContext ctx, PlayerState p, string itemId, UnitInstance unit,
            bool boardLocked) {
            if (unit == null || !p.Units.Contains(unit)) return CommandError.InvalidUnit;
            var stack = Find(p, itemId);
            if (stack == null || stack.Count <= 0) return CommandError.InvalidItem;
            if (boardLocked && unit.Location.IsBoard) return CommandError.WrongPhase;
            if (!TryEquip(ctx, unit, itemId)) return CommandError.ItemSlotsFull;

            stack.Count--;
            if (stack.Count == 0) p.Items.Remove(stack);
            ctx.Events.Add(new ItemBenchChangedEvent { PlayerId = p.Id });
            ctx.Events.Add(new UnitItemsChangedEvent { PlayerId = p.Id, UnitId = unit.Id });
            return CommandResult.Success;
        }
    }
}
