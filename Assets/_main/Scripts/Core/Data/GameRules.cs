namespace AutoChess.Core {
    /// <summary>
    /// Every tunable number of the game (was GameConfigs + the constants of HeroTrait, Shop, LineUp, Deck,
    /// HeroInventory...). The defaults reproduce the original single-player game exactly.
    /// </summary>
    public sealed class GameRules {
        // ---- simulation clock
        public int TicksPerSecond = 30;
        public double TickDelta => 1d / TicksPerSecond;
        public int SecondsToTicks(double seconds) => seconds >= int.MaxValue / (double)TicksPerSecond
            ? int.MaxValue
            : (int)System.Math.Ceiling(seconds * TicksPerSecond - 1e-4);

        // ---- match flow
        public double SetupSeconds = 99999;
        public double PreparationSeconds = 99999;
        public double TransitionSeconds = 5;
        public double BattleSeconds = 60;
        public double SummarySeconds = 2;
        /// <summary>A ready vote (or the old Space shortcut) shortens the current phase to this.</summary>
        public double ReadyRemainingSeconds = 1;

        // ---- roster
        public int BenchSize = 9;
        public int BoardRows = 8;
        public int BoardColumns = 8;
        /// <summary>A player deploys on rows [0, BoardRows / 2).</summary>
        public int PlayerRows => BoardRows / 2;
        public int UnitsToRankUp = 3;
        public int ItemCapacity = 3;

        // ---- economy
        public int StartingGold = 0;
        public int XpPerRound = 2;
        public int XpPerPurchase = 4;
        public int XpPurchaseCost = 4;
        public int RefreshCost = 2;
        public int ShopSlots = 5;
        public int StarterOfferCount = 3;
        /// <summary>Indexed by (int)Reputation.</summary>
        public int[] UnitPrices = { 0, 1, 3, 5 };

        public LevelDefinition[] Levels = {
            new() { XpToNext = 2, MaxUnitsOnBoard = 1, ShopOdds = new[] { 100, 0, 0 } },
            new() { XpToNext = 4, MaxUnitsOnBoard = 2, ShopOdds = new[] { 90, 10, 0 } },
            new() { XpToNext = 8, MaxUnitsOnBoard = 3, ShopOdds = new[] { 79, 20, 1 } },
            new() { XpToNext = 16, MaxUnitsOnBoard = 4, ShopOdds = new[] { 67, 30, 3 } },
            new() { XpToNext = 32, MaxUnitsOnBoard = 5, ShopOdds = new[] { 50, 45, 5 } },
            new() { XpToNext = 64, MaxUnitsOnBoard = 6, ShopOdds = new[] { 30, 60, 10 } },
            new() { XpToNext = 128, MaxUnitsOnBoard = 7, ShopOdds = new[] { 15, 70, 15 } },
            new() { XpToNext = 256, MaxUnitsOnBoard = 8, ShopOdds = new[] { 5, 65, 30 } },
            new() { XpToNext = -1, MaxUnitsOnBoard = 9, ShopOdds = new[] { 1, 49, 50 } },
        };

        public int MaxLevel => Levels.Length;
        public LevelDefinition Level(int level) => Levels[System.Math.Clamp(level, 1, Levels.Length) - 1];
        public int PriceOf(Reputation reputation) => UnitPrices[(int)reputation];

        // ---- combat
        public double BaseCriticalChance = 0.15;
        public double BaseCriticalDamage = 1.5;
        public double BaseEnergyRegenEfficiency = 1;
        public double BasePenetration = 0;
        public double BaseLifeSteal = 0;
        public double BaseTenacity = 0;
        public double MaxEnergy = 100;
        public double DamageReductionConstant = 100;
        public double MaxCriticalChance = 1;
        public double MaxPenetration = 0.7;
        public double MaxLifeSteal = 1;
        public double MaxTenacity = 0.5;
        public double AntiHealEfficiency = 0.5;
        public double MinArmorAndResistance = 1;
        public double MinDamageDealt = 1;
        public double MinDamage = 1;
        public double MinAttackSpeed = 0.1;
        /// <summary>Radius of a unit's hit sphere (the Hero prefab's SphereCollider).</summary>
        public double UnitRadius = 0.8;
        public double ProjectileSpeed = 20;

        // ---- board geometry (overridden by the scene's Map component on the client)
        public double CellWidth = 1.88;
        public double CellHeight = 1.89;
    }
}
