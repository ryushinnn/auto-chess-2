namespace AutoChess.Core {
    /// <summary>Unit shop: odds by level, bad-luck protection, refresh, lock and purchase.</summary>
    public static class Shop {
        public static void Roll(MatchContext ctx, PlayerState p) {
            var shop = p.Shop;
            var odds = ctx.Rules.Level(p.Level).ShopOdds;
            var legendaryOdds = odds.Length > 0 ? odds[odds.Length - 1] : 0;

            for (var i = 0; i < shop.Slots.Length; i++) {
                if (shop.Slots[i] != null) ctx.Pool.Release(shop.Slots[i], 1);

                var reputation = RollReputation(p.Rng, odds);
                if (reputation == Reputation.Legendary) {
                    shop.Insurance = 0;
                }
                else {
                    shop.Insurance += legendaryOdds;
                    if (shop.Insurance >= 100) {
                        reputation = Reputation.Legendary;
                        shop.Insurance = 0;
                    }
                }
                shop.Slots[i] = ctx.Pool.Roll(reputation, p.Rng);
            }
            ctx.Events.Add(new ShopChangedEvent { PlayerId = p.Id, Rerolled = true, Locked = shop.Locked });
        }

        static Reputation RollReputation(DeterministicRandom rng, int[] odds) {
            var roll = rng.NextFloat();
            var total = 0f;
            for (var i = 0; i < odds.Length; i++) {
                total += odds[i] / 100f;
                if (roll <= total) return (Reputation)(i + 1);
            }
            return (Reputation)odds.Length;
        }

        /// <summary>Free roll at the start of a preparation, unless the shop is locked.</summary>
        public static void AutoRefresh(MatchContext ctx, PlayerState p) {
            if (!p.Shop.Locked) Roll(ctx, p);
        }

        /// <summary>Paid reroll. Rerolling also unlocks the shop.</summary>
        public static CommandResult Refresh(MatchContext ctx, PlayerState p) {
            if (!Economy.TrySpend(ctx, p, ctx.Rules.RefreshCost)) return CommandError.NotEnoughGold;
            p.Shop.Locked = false;
            Roll(ctx, p);
            return CommandResult.Success;
        }

        public static void SetLocked(MatchContext ctx, PlayerState p, bool locked) {
            if (p.Shop.Locked == locked) return;
            p.Shop.Locked = locked;
            ctx.Events.Add(new ShopChangedEvent { PlayerId = p.Id, Rerolled = false, Locked = locked });
        }

        public static CommandResult Buy(MatchContext ctx, PlayerState p, int slot, bool boardLocked) {
            var slots = p.Shop.Slots;
            if (slot < 0 || slot >= slots.Length) return CommandError.InvalidSlot;
            var unitId = slots[slot];
            if (unitId == null) return CommandError.ShopSlotEmpty;

            var price = ctx.Rules.PriceOf(ctx.Db.Unit(unitId).Reputation);
            if (p.Gold < price) return CommandError.NotEnoughGold;
            if (Roster.AddUnit(ctx, p, unitId, boardLocked) == null) return CommandError.BenchFull;

            Economy.TrySpend(ctx, p, price);
            slots[slot] = null;
            ctx.Events.Add(new ShopChangedEvent { PlayerId = p.Id, Rerolled = false, Locked = p.Shop.Locked });
            return CommandResult.Success;
        }
    }
}
