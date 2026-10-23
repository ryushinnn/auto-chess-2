namespace AutoChess.Core {
    /// <summary>Round rewards and the starter pack (was Progress.ClaimReward + StarterPackUI).</summary>
    public static class Rewards {
        public static void Grant(MatchContext ctx, PlayerState p, RewardDefinition reward, bool boardLocked,
            ref int gold, ref int units, ref int items) {
            var rng = p.Rng;
            switch (reward.Kind) {
                case RewardKind.Coin:
                    Economy.GainGold(ctx, p, reward.Coins);
                    gold += reward.Coins;
                    break;

                case RewardKind.RandomCoin: {
                    var coins = rng.Range(reward.MinCoins, reward.MaxCoins + 1);
                    Economy.GainGold(ctx, p, coins);
                    gold += coins;
                    break;
                }

                case RewardKind.Unit: {
                    var pool = ctx.Db.ShopPool(reward.Reputation);
                    if (pool.Count == 0) break;
                    var unit = pool[rng.Range(0, pool.Count)];
                    if (Roster.AddUnit(ctx, p, unit.Id, boardLocked) != null) units++;
                    break;
                }

                case RewardKind.RawItem: {
                    var list = ctx.Db.Components;
                    if (list.Count == 0) break;
                    Items.AddToBench(ctx, p, list[rng.Range(0, list.Count)].Id);
                    items++;
                    break;
                }

                case RewardKind.ForgedItem: {
                    var list = ctx.Db.CombinedItems;
                    if (list.Count == 0) break;
                    Items.AddToBench(ctx, p, list[rng.Range(0, list.Count)].Id);
                    items++;
                    break;
                }

                case RewardKind.Random: {
                    var options = reward.Options;
                    if (options.Length == 0) break;
                    var roll = rng.NextFloat();
                    var total = 0f;
                    RewardDefinition picked = null;
                    foreach (var o in options) {
                        total += o.Rate / 100f;
                        if (roll <= total) {
                            picked = o.Reward;
                            break;
                        }
                    }
                    picked ??= options[options.Length - 1].Reward;
                    if (picked != null) Grant(ctx, p, picked, boardLocked, ref gold, ref units, ref items);
                    break;
                }
            }
        }

        public static void GrantPending(MatchContext ctx, PlayerState p) {
            if (p.PendingRewards.Count == 0) return;
            int gold = 0, units = 0, items = 0;
            foreach (var r in p.PendingRewards) Grant(ctx, p, r, false, ref gold, ref units, ref items);
            p.PendingRewards.Clear();
            ctx.Events.Add(new RewardsGrantedEvent { PlayerId = p.Id, Gold = gold, Units = units, Items = items });
        }

        // ---- starter pack: a choice of (cheap unit + component) offers, each rerollable for free

        public static void RollStarterOffer(MatchContext ctx, PlayerState p, int index) {
            var pool = ctx.Db.ShopPool(Reputation.Unknown);
            var components = ctx.Db.Components;
            var offer = p.StarterOffers[index];
            offer.UnitId = pool.Count > 0 ? pool[p.Rng.Range(0, pool.Count)].Id : null;
            offer.ItemId = components.Count > 0 ? components[p.Rng.Range(0, components.Count)].Id : null;
        }

        public static void RollStarterOffers(MatchContext ctx, PlayerState p) {
            for (var i = 0; i < p.StarterOffers.Length; i++) RollStarterOffer(ctx, p, i);
            ctx.Events.Add(new StarterOffersChangedEvent { PlayerId = p.Id, OfferIndex = -1 });
        }

        public static CommandResult RerollStarterOffer(MatchContext ctx, PlayerState p, int index) {
            if (p.StarterChosen) return CommandError.AlreadyChosen;
            if (index < 0 || index >= p.StarterOffers.Length) return CommandError.InvalidSlot;
            RollStarterOffer(ctx, p, index);
            ctx.Events.Add(new StarterOffersChangedEvent { PlayerId = p.Id, OfferIndex = index });
            return CommandResult.Success;
        }

        public static CommandResult ChooseStarterOffer(MatchContext ctx, PlayerState p, int index) {
            if (p.StarterChosen) return CommandError.AlreadyChosen;
            if (index < 0 || index >= p.StarterOffers.Length) return CommandError.InvalidSlot;
            var offer = p.StarterOffers[index];
            if (offer.UnitId != null) Roster.AddUnit(ctx, p, offer.UnitId, false);
            if (offer.ItemId != null) Items.AddToBench(ctx, p, offer.ItemId);
            p.StarterChosen = true;
            return CommandResult.Success;
        }
    }
}
