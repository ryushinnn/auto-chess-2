namespace AutoChess.Core {
    /// <summary>
    /// Every tunable number of the game (was GameConfigs + the constants of HeroTrait, Shop, LineUp, Deck,
    /// HeroInventory...). Defaults follow Teamfight Tactics where the game has the same mechanic (phase clocks,
    /// player health and damage, income, interest, streaks, xp curve, unit pool), scaled to this game's three
    /// unit tiers (Unknown 1 gold, Elite 3, Legendary 5).
    /// </summary>
    public sealed class GameRules {
        // ---- simulation clock
        public int TicksPerSecond = 30;
        public double TickDelta => 1d / TicksPerSecond;
        public int SecondsToTicks(double seconds) => seconds >= int.MaxValue / (double)TicksPerSecond
            ? int.MaxValue
            : (int)System.Math.Ceiling(seconds * TicksPerSecond - 1e-4);

        // ---- match flow
        public double SetupSeconds = 30;
        public double PreparationSeconds = 30;
        public double TransitionSeconds = 4;
        /// <summary>
        /// A battle still running then is a draw. TFT allows 30 s + 15 s of overtime; this game's units hit slower
        /// (attack speed 0.2 - 0.6), so its fights take 20 - 50 s and keep the original minute.
        /// </summary>
        public double BattleSeconds = 60;
        public double SummarySeconds = 3;
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

        // ---- players
        public int StartingHealth = 100;
        /// <summary>Damage to the loser of a round, by stage (1-based; the last value repeats), plus one per surviving enemy unit.</summary>
        public int[] StageDamage = { 0, 2, 5, 8, 10, 12, 17 };
        public int DamagePerSurvivingUnit = 1;
        /// <summary>
        /// Rounds of a stage once the authored stages (all PvE) are played, in matches with 2+ players: every round
        /// is PvP except <see cref="PveRoundOfStage"/>, which replays an authored PvE round, stronger each stage.
        /// </summary>
        public int RoundsPerStage = 7;
        public int PveRoundOfStage = 7;
        /// <summary>HP and damage of the creeps of a generated PvE round grow by this factor per stage after the authored ones.</summary>
        public double PveScalingPerStage = 1.6;
        /// <summary>A match never runs longer than this many stages (the healthiest players then share the top places).</summary>
        public int MaxStages = 10;

        // ---- economy
        public int StartingGold = 0;
        /// <summary>Gold at the start of each preparation after the first, by round number (0-based, all stages); then <see cref="BaseIncome"/>.</summary>
        public int[] EarlyIncome = { 0, 2, 2, 3, 4 };
        public int BaseIncome = 5;
        /// <summary>1 gold per this much gold banked, up to <see cref="MaxInterest"/>.</summary>
        public int InterestStep = 10;
        public int MaxInterest = 5;
        /// <summary>Bonus gold by win / loss streak length (PvP rounds); the last value repeats.</summary>
        public int[] StreakGold = { 0, 0, 1, 1, 1, 2, 3 };
        public int PvpWinGold = 1;
        /// <summary>Copies of each unit in the pool shared by a match's players, indexed by (int)Reputation.</summary>
        public int[] PoolCopies = { 0, 29, 18, 10 };
        public int XpPerRound = 2;
        public int XpPerPurchase = 4;
        public int XpPurchaseCost = 4;
        public int RefreshCost = 2;
        public int ShopSlots = 5;
        public int StarterOfferCount = 3;
        /// <summary>Indexed by (int)Reputation.</summary>
        public int[] UnitPrices = { 0, 1, 3, 5 };

        /// <summary>
        /// TFT's xp curve and shop odds, its five unit costs folded onto three tiers (1-2 cost = Unknown,
        /// 3 = Elite, 4-5 = Legendary).
        /// </summary>
        public LevelDefinition[] Levels = {
            new() { XpToNext = 2, MaxUnitsOnBoard = 1, ShopOdds = new[] { 100, 0, 0 } },
            new() { XpToNext = 2, MaxUnitsOnBoard = 2, ShopOdds = new[] { 100, 0, 0 } },
            new() { XpToNext = 6, MaxUnitsOnBoard = 3, ShopOdds = new[] { 100, 0, 0 } },
            new() { XpToNext = 10, MaxUnitsOnBoard = 4, ShopOdds = new[] { 85, 15, 0 } },
            new() { XpToNext = 20, MaxUnitsOnBoard = 5, ShopOdds = new[] { 78, 20, 2 } },
            new() { XpToNext = 36, MaxUnitsOnBoard = 6, ShopOdds = new[] { 70, 25, 5 } },
            new() { XpToNext = 48, MaxUnitsOnBoard = 7, ShopOdds = new[] { 49, 40, 11 } },
            new() { XpToNext = 76, MaxUnitsOnBoard = 8, ShopOdds = new[] { 43, 32, 25 } },
            new() { XpToNext = -1, MaxUnitsOnBoard = 9, ShopOdds = new[] { 30, 30, 40 } },
        };

        public int MaxLevel => Levels.Length;
        public LevelDefinition Level(int level) => Levels[System.Math.Clamp(level, 1, Levels.Length) - 1];
        public int PriceOf(Reputation reputation) => UnitPrices[(int)reputation];
        public int DamageOfStage(int stageIndex) => StageDamage[System.Math.Clamp(stageIndex, 0, StageDamage.Length - 1)];
        public int StreakBonus(int streak) => StreakGold[System.Math.Min(System.Math.Abs(streak), StreakGold.Length - 1)];
        public int IncomeOfRound(int roundNumber) => roundNumber < EarlyIncome.Length ? EarlyIncome[roundNumber] : BaseIncome;

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
