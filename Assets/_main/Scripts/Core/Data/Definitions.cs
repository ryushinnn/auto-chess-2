using System;

namespace AutoChess.Core {
    // Immutable, engine-free game data. Built from the ScriptableObject authoring assets on the client
    // (AutoChess.Game.GameDataBuilder) and could be loaded from JSON by a dedicated server.

    public sealed class UnitDefinition {
        public string Id;
        public string Name;
        public bool Summoned;
        public Realm Realm;
        public Role Roles;
        public Reputation Reputation;
        public bool Moveable = true;

        public double MaxHp;
        public double PhysicalDamage;
        public double MagicalDamage;
        public double Armor;
        public double Resistance;
        public double AttackSpeed;
        public double MovementSpeed;
        public double EnergyRegenPerAttack;
        public double EnergyRegenPerHit;
        public int AttackRange = 1;

        public bool Unstoppable;
        /// <summary>Evolution bonuses indexed by (int)HeroRank. Missing ranks have an empty array.</summary>
        public StatModifier[][] Evolutions = Array.Empty<StatModifier[]>();
        public double[] SkillParams = Array.Empty<double>();
        public string[] SkillParamKeys = Array.Empty<string>();
        public string[] SpecialKeys = Array.Empty<string>();

        public StatModifier[] EvolutionFor(HeroRank rank) {
            var i = (int)rank;
            return i < Evolutions.Length && Evolutions[i] != null ? Evolutions[i] : Array.Empty<StatModifier>();
        }

        public double Param(int index) {
            if (index < 0 || index >= SkillParams.Length) {
                throw new IndexOutOfRangeException($"{Id}: skill param #{index} missing ({SkillParams.Length} defined)");
            }
            return SkillParams[index];
        }

        public string SpecialKey(int index) {
            if (index < 0 || index >= SpecialKeys.Length) {
                throw new IndexOutOfRangeException($"{Id}: special key #{index} missing ({SpecialKeys.Length} defined)");
            }
            return SpecialKeys[index];
        }

        public override string ToString() => Id;
    }

    public sealed class ItemDefinition {
        public string Id;
        public string Name;
        /// <summary>Two component ids for a combined (forged) item, empty for a component.</summary>
        public string[] Ingredients = Array.Empty<string>();
        public StatModifier[] Modifiers = Array.Empty<StatModifier>();

        public bool IsCombined => Ingredients.Length == 2;
        public override string ToString() => Id;
    }

    public enum TraitKind {
        Role,
        Realm,
    }

    public sealed class TraitDefinition {
        public TraitKind Kind;
        public Role Role;
        public Realm Realm;
        public string Name;
        public int[] Checkpoints = Array.Empty<int>();
        public double[] Params = Array.Empty<double>();

        /// <summary>Highest checkpoint reached by <paramref name="count"/> units, or -1.</summary>
        public int TierFor(int count) {
            for (var i = Checkpoints.Length - 1; i >= 0; i--) {
                if (count >= Checkpoints[i]) return i;
            }
            return -1;
        }

        public double Param(int index) {
            if (index < 0 || index >= Params.Length) {
                throw new IndexOutOfRangeException($"{this}: trait param #{index} missing ({Params.Length} defined)");
            }
            return Params[index];
        }

        public TraitId Id => Kind == TraitKind.Role ? TraitId.Of(Role) : TraitId.Of(Realm);
        public override string ToString() => Kind == TraitKind.Role ? Role.ToString() : Realm.ToString();
    }

    /// <summary>A role or a realm, usable as a dictionary key.</summary>
    [Serializable]
    public readonly struct TraitId : IEquatable<TraitId>, IComparable<TraitId> {
        public readonly TraitKind Kind;
        public readonly int Value;

        TraitId(TraitKind kind, int value) {
            Kind = kind;
            Value = value;
        }

        public static TraitId Of(Role role) => new(TraitKind.Role, (int)role);
        public static TraitId Of(Realm realm) => new(TraitKind.Realm, (int)realm);

        public Role Role => Kind == TraitKind.Role ? (Role)Value : Role.None;
        public Realm Realm => Kind == TraitKind.Realm ? (Realm)Value : Realm.None;

        public bool Equals(TraitId other) => Kind == other.Kind && Value == other.Value;
        public override bool Equals(object obj) => obj is TraitId other && Equals(other);
        public override int GetHashCode() => ((int)Kind << 16) ^ Value;
        public int CompareTo(TraitId other) => Kind != other.Kind ? Kind.CompareTo(other.Kind) : Value.CompareTo(other.Value);
        public override string ToString() => Kind == TraitKind.Role ? Role.ToString() : Realm.ToString();
    }

    public sealed class StageDefinition {
        public string Name;
        public RoundDefinition[] Rounds = Array.Empty<RoundDefinition>();
    }

    public sealed class RoundDefinition {
        public EnemyDefinition[] Enemies = Array.Empty<EnemyDefinition>();
        public RewardDefinition[] Rewards = Array.Empty<RewardDefinition>();
        public RewardDefinition[] WinRewards = Array.Empty<RewardDefinition>();
    }

    public sealed class EnemyDefinition {
        public string UnitId;
        public HeroRank Rank;
        /// <summary>Cell on the full 8x8 battle grid (rows 4..7 are the away half).</summary>
        public HexCoord Cell;
        public string[] Items = Array.Empty<string>();
        public double HpScale = 1;
        public double DamageScale = 1;
    }

    public enum RewardKind {
        Coin,
        RandomCoin,
        Unit,
        RawItem,
        ForgedItem,
        Random,
    }

    public sealed class RewardDefinition {
        public RewardKind Kind;
        public int Coins;
        public int MinCoins;
        public int MaxCoins;
        public Reputation Reputation;
        public WeightedReward[] Options = Array.Empty<WeightedReward>();
    }

    public sealed class WeightedReward {
        public RewardDefinition Reward;
        /// <summary>Percent. The options are rolled in order against a cumulative rate; the last one is the fallback.</summary>
        public int Rate;
    }

    public sealed class LevelDefinition {
        /// <summary>-1 at the max level.</summary>
        public int XpToNext;
        public int MaxUnitsOnBoard;
        /// <summary>Shop odds in percent, indexed by reputation - 1 (Unknown, Elite, Legendary).</summary>
        public int[] ShopOdds = Array.Empty<int>();
    }
}
