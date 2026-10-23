using System;
using System.Collections.Generic;

namespace AutoChess.Core.Combat {
    /// <summary>
    /// Stat formula shared by battle units and the out-of-battle unit info panel. Per stat: start from the
    /// base, apply flat modifiers (largest first) then percentages (largest first, compounding), clamping
    /// after every step - exactly what HeroAttributes.RecalculateAttributes did.
    /// </summary>
    public static class UnitStats {
        public static float BaseValue(GameRules rules, UnitDefinition def, StatKind stat, float hpScale, float damageScale) {
            switch (stat) {
                case StatKind.MaxHp: return def.MaxHp * hpScale;
                case StatKind.PhysicalDamage: return def.PhysicalDamage * damageScale;
                case StatKind.MagicalDamage: return def.MagicalDamage * damageScale;
                case StatKind.Armor: return def.Armor;
                case StatKind.Resistance: return def.Resistance;
                case StatKind.AttackSpeed: return def.AttackSpeed;
                case StatKind.CriticalChance: return rules.BaseCriticalChance;
                case StatKind.CriticalDamage: return rules.BaseCriticalDamage;
                case StatKind.EnergyRegenEfficiency: return rules.BaseEnergyRegenEfficiency;
                case StatKind.PhysicalPenetration:
                case StatKind.MagicalPenetration: return rules.BasePenetration;
                case StatKind.LifeSteal: return rules.BaseLifeSteal;
                case StatKind.Tenacity: return rules.BaseTenacity;
                default: throw new ArgumentOutOfRangeException(nameof(stat), stat, null);
            }
        }

        /// <summary>Sorts <paramref name="modifiers"/> (all for one stat) and applies them to the base value.</summary>
        public static float Apply(GameRules rules, StatKind stat, float value, List<StatModifier> modifiers) {
            modifiers.Sort(Compare);
            foreach (var m in modifiers) {
                var delta = m.Type == ModifierType.Flat ? m.Value : value * m.Value;
                switch (stat) {
                    case StatKind.PhysicalDamage:
                    case StatKind.MagicalDamage:
                        value = Math.Max(value + delta, rules.MinDamage);
                        break;
                    case StatKind.Armor:
                    case StatKind.Resistance:
                        value = Math.Max(value + delta, rules.MinArmorAndResistance);
                        break;
                    case StatKind.AttackSpeed:
                        value = Math.Max(value + delta, rules.MinAttackSpeed);
                        break;
                    case StatKind.PhysicalPenetration:
                    case StatKind.MagicalPenetration:
                        value = Math.Min(value + delta, rules.MaxPenetration);
                        break;
                    case StatKind.LifeSteal:
                        value = Math.Min(value + delta, rules.MaxLifeSteal);
                        break;
                    case StatKind.Tenacity:
                        value = Math.Min(value + delta, rules.MaxTenacity);
                        break;
                    default:
                        value += delta;
                        break;
                }
            }
            if (stat == StatKind.CriticalChance) value = Math.Min(value, rules.MaxCriticalChance);
            return value;
        }

        static int Compare(StatModifier a, StatModifier b) {
            if (a.Type != b.Type) return a.Type == ModifierType.Flat ? -1 : 1;
            return b.Value.CompareTo(a.Value);
        }

        /// <summary>Stats of an owned unit outside battle: base + rank evolution + items.</summary>
        public static void Compute(GameDatabase db, UnitDefinition def, HeroRank rank, IReadOnlyList<string> items,
            StatBlock result) {
            var rules = db.Rules;
            var buffer = new List<StatModifier>();
            for (var s = 0; s < StatBlock.Count; s++) {
                var stat = (StatKind)s;
                buffer.Clear();
                foreach (var m in def.EvolutionFor(rank)) {
                    if (m.Stat == stat) buffer.Add(m);
                }
                if (items != null) {
                    foreach (var id in items) {
                        foreach (var m in db.Item(id).Modifiers) {
                            if (m.Stat == stat) buffer.Add(m);
                        }
                    }
                }
                result[stat] = Apply(rules, stat, BaseValue(rules, def, stat, 1, 1), buffer);
            }
        }
    }
}
