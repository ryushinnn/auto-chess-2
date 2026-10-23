using System;
using System.Collections.Generic;

namespace AutoChess.Core {
    public enum StatKind {
        MaxHp,
        PhysicalDamage,
        MagicalDamage,
        Armor,
        Resistance,
        AttackSpeed,
        CriticalChance,
        CriticalDamage,
        EnergyRegenEfficiency,
        PhysicalPenetration,
        MagicalPenetration,
        LifeSteal,
        Tenacity,
    }

    /// <summary>Order matches the serialized AttributeModifier.Type (0 = fixed value, 1 = percentage).</summary>
    public enum ModifierType {
        Flat = 0,
        Percent = 1,
    }

    [Serializable]
    public readonly struct StatModifier {
        public readonly StatKind Stat;
        public readonly double Value;
        public readonly ModifierType Type;

        public StatModifier(StatKind stat, double value, ModifierType type) {
            Stat = stat;
            Value = value;
            Type = type;
        }

        public override string ToString() => Type == ModifierType.Flat ? $"{Stat}+{Value}" : $"{Stat}+{Value * 100}%";
    }

    /// <summary>String keys used by the authoring data ("max_hp", "atk_spd"...).</summary>
    public static class StatKeys {
        public const string MaxHp = "max_hp";
        public const string PhysicalDamage = "p_dmg";
        public const string MagicalDamage = "m_dmg";
        public const string Armor = "armor";
        public const string Resistance = "resistance";
        public const string AttackSpeed = "atk_spd";
        public const string CriticalChance = "crit_chance";
        public const string CriticalDamage = "crit_dmg";
        public const string EnergyRegenEfficiency = "energy_regen_eff";
        public const string PhysicalPenetration = "p_pen";
        public const string MagicalPenetration = "m_pen";
        public const string LifeSteal = "life_steal";
        public const string Tenacity = "tenacity";

        static readonly Dictionary<string, StatKind> ByKey = new() {
            { MaxHp, StatKind.MaxHp },
            { PhysicalDamage, StatKind.PhysicalDamage },
            { MagicalDamage, StatKind.MagicalDamage },
            { Armor, StatKind.Armor },
            { Resistance, StatKind.Resistance },
            { AttackSpeed, StatKind.AttackSpeed },
            { CriticalChance, StatKind.CriticalChance },
            { CriticalDamage, StatKind.CriticalDamage },
            { EnergyRegenEfficiency, StatKind.EnergyRegenEfficiency },
            { PhysicalPenetration, StatKind.PhysicalPenetration },
            { MagicalPenetration, StatKind.MagicalPenetration },
            { LifeSteal, StatKind.LifeSteal },
            { Tenacity, StatKind.Tenacity },
        };

        public static bool TryParse(string key, out StatKind stat) => ByKey.TryGetValue(key ?? string.Empty, out stat);

        public static StatKind Parse(string key) {
            if (TryParse(key, out var stat)) return stat;
            throw new ArgumentException($"Unknown stat key '{key}'");
        }
    }

    /// <summary>All stats of a unit, indexed by <see cref="StatKind"/>.</summary>
    public sealed class StatBlock {
        public const int Count = (int)StatKind.Tenacity + 1;
        readonly double[] values = new double[Count];

        public double this[StatKind stat] {
            get => values[(int)stat];
            set => values[(int)stat] = value;
        }

        public void CopyFrom(StatBlock other) => Array.Copy(other.values, values, Count);
    }
}
