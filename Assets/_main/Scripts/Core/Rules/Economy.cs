namespace AutoChess.Core {
    /// <summary>Gold, experience and levels (was Inventory coins + Level).</summary>
    public static class Economy {
        public static void GainGold(MatchContext ctx, PlayerState p, int amount) {
            if (amount == 0) return;
            p.Gold += amount;
            ctx.Events.Add(new GoldChangedEvent { PlayerId = p.Id, Gold = p.Gold, Delta = amount });
        }

        public static bool TrySpend(MatchContext ctx, PlayerState p, int amount) {
            if (p.Gold < amount) return false;
            GainGold(ctx, p, -amount);
            return true;
        }

        public static bool IsMaxLevel(GameRules rules, PlayerState p) => p.Level >= rules.MaxLevel;

        public static int XpToNext(GameRules rules, PlayerState p) => rules.Level(p.Level).XpToNext;

        public static int MaxBoardUnits(GameRules rules, PlayerState p) => rules.Level(p.Level).MaxUnitsOnBoard;

        /// <summary>Adds experience and levels up as many times as it pays for. False at the max level.</summary>
        public static bool GainXp(MatchContext ctx, PlayerState p, int amount) {
            var rules = ctx.Rules;
            if (IsMaxLevel(rules, p)) return false;

            p.Xp += amount;
            var leveled = false;
            while (!IsMaxLevel(rules, p) && p.Xp >= XpToNext(rules, p)) {
                p.Xp -= XpToNext(rules, p);
                p.Level++;
                leveled = true;
            }

            ctx.Events.Add(new XpChangedEvent {
                PlayerId = p.Id, Level = p.Level, Xp = p.Xp,
                XpToNext = IsMaxLevel(rules, p) ? -1 : XpToNext(rules, p), LeveledUp = leveled
            });
            if (leveled) {
                ctx.Events.Add(new BoardLimitChangedEvent { PlayerId = p.Id, Count = p.BoardCount, Max = MaxBoardUnits(rules, p) });
            }
            return true;
        }

        public static CommandResult BuyXp(MatchContext ctx, PlayerState p) {
            var rules = ctx.Rules;
            if (p.Gold < rules.XpPurchaseCost) return CommandError.NotEnoughGold;
            if (!GainXp(ctx, p, rules.XpPerPurchase)) return CommandError.MaxLevel;
            TrySpend(ctx, p, rules.XpPurchaseCost);
            return CommandResult.Success;
        }
    }
}
