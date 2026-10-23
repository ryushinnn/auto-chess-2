namespace AutoChess.Core {
    /// <summary>
    /// Every tunable number of the game (was GameConfigs + the constants of HeroTrait, Shop, LineUp, Deck,
    /// HeroInventory...). The defaults reproduce the original single-player game exactly.
    /// </summary>
    public sealed class GameRules {
        // ---- simulation clock
        public int TicksPerSecond = 30;
        public float TickDelta => 1f / TicksPerSecond;
        public int SecondsToTicks(float seconds) => seconds >= int.MaxValue / (float)TicksPerSecond
            ? int.MaxValue
            : (int)System.Math.Ceiling(seconds * TicksPerSecond - 1e-4);

        // ---- match flow
        public float SetupSeconds = 99999;
        public float PreparationSeconds = 99999;
        public float TransitionSeconds = 5;
        public float BattleSeconds = 60;
        public float SummarySeconds = 2;
        /// <summary>A ready vote (or the old Space shortcut) shortens the current phase to this.</summary>
        public float ReadyRemainingSeconds = 1;

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
        public float BaseCriticalChance = 0.15f;
        public float BaseCriticalDamage = 1.5f;
        public float BaseEnergyRegenEfficiency = 1;
        public float BasePenetration = 0;
        public float BaseLifeSteal = 0;
        public float BaseTenacity = 0;
        public float MaxEnergy = 100;
        public float DamageReductionConstant = 100;
        public float MaxCriticalChance = 1;
        public float MaxPenetration = 0.7f;
        public float MaxLifeSteal = 1;
        public float MaxTenacity = 0.5f;
        public float AntiHealEfficiency = 0.5f;
        public float MinArmorAndResistance = 1;
        public float MinDamageDealt = 1;
        public float MinDamage = 1;
        public float MinAttackSpeed = 0.1f;
        /// <summary>Radius of a unit's hit sphere (the Hero prefab's SphereCollider).</summary>
        public float UnitRadius = 0.8f;
        public float ProjectileSpeed = 20;

        // ---- board geometry (overridden by the scene's Map component on the client)
        public float CellWidth = 1.88f;
        public float CellHeight = 1.89f;
    }
}
