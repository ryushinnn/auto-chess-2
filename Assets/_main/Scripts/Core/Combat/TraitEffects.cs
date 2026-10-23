using System;
using System.Collections.Generic;

namespace AutoChess.Core.Combat {
    /// <summary>Battle effect of an active synergy (was DestinyProcessor).</summary>
    public abstract class TraitEffect {
        public abstract void Apply(BattleSimulation battle, TeamSide side, TraitDefinition def, int tier);
    }

    public static class TraitEffects {
        static readonly Dictionary<TraitId, Func<TraitEffect>> Factories = new() {
            { TraitId.Of(Role.Marksman), () => new MarksmanTrait() },
            { TraitId.Of(Realm.Mortal), () => new MortalTrait() },
        };

        public static void Register(TraitId id, Func<TraitEffect> factory) => Factories[id] = factory;

        /// <summary>Activates every reached synergy of a team, counted over its distinct unit definitions.</summary>
        public static void Apply(BattleSimulation battle, TeamSide side) {
            var ids = new List<string>();
            foreach (var u in battle.AliveUnits(side)) ids.Add(u.Def.Id);
            foreach (var status in Traits.Compute(battle.Db, ids)) {
                if (!status.Active || !Factories.TryGetValue(status.Id, out var factory)) continue;
                var def = battle.Db.Trait(status.Id);
                if (def != null) factory().Apply(battle, side, def, status.Tier);
            }
        }
    }

    /// <summary>
    /// Marksmen gain attack speed; when one dies the surviving marksmen get the same bonus again for a few
    /// seconds. Params: [0..2] attack speed per tier, [3..5] burst duration per tier.
    /// </summary>
    public sealed class MarksmanTrait : TraitEffect {
        public override void Apply(BattleSimulation battle, TeamSide side, TraitDefinition def, int tier) {
            var bonus = def.Param(tier);
            var burst = def.Param(3 + tier);
            var marksmen = new List<CombatUnit>();
            foreach (var u in battle.AliveUnits(side)) {
                if (u.Def.Roles.HasRole(Role.Marksman)) marksmen.Add(u);
            }
            foreach (var u in marksmen) {
                u.AddModifierSet(ModifierSet.Permanent(u, "marksman",
                    new[] { new StatModifier(StatKind.AttackSpeed, bonus, ModifierType.Percent) }, mark: false));
            }

            battle.UnitDied += (dead, _) => {
                if (!marksmen.Contains(dead)) return;
                foreach (var other in marksmen) {
                    if (other == dead || !other.Alive) continue;
                    other.AddModifierSet(ModifierSet.Timed(dead, "marksman_burst", burst,
                        new[] { new StatModifier(StatKind.AttackSpeed, bonus, ModifierType.Percent) }, mark: false));
                }
            };
        }
    }

    /// <summary>
    /// Mortal heroes each gain their own stat, scaled by the tier. Params: [0..3] tier multipliers, then the
    /// bonus of Akali (magic damage), Jinx (physical damage), Caitlyn (energy regen), Katarina (crit chance),
    /// Tristana (crit damage), Miss Fortune (penetration).
    /// </summary>
    public sealed class MortalTrait : TraitEffect {
        public override void Apply(BattleSimulation battle, TeamSide side, TraitDefinition def, int tier) {
            var mul = 1 + def.Param(tier);
            foreach (var hero in battle.AliveUnits(side)) {
                StatModifier[] mods;
                switch (hero.Def.Id) {
                    case HeroIds.Akali:
                        mods = new[] { new StatModifier(StatKind.MagicalDamage, def.Param(4) * mul, ModifierType.Flat) };
                        break;
                    case HeroIds.Jinx:
                        mods = new[] { new StatModifier(StatKind.PhysicalDamage, def.Param(5) * mul, ModifierType.Flat) };
                        break;
                    case HeroIds.Caitlyn:
                        mods = new[] { new StatModifier(StatKind.EnergyRegenEfficiency, def.Param(6) * mul, ModifierType.Flat) };
                        break;
                    case HeroIds.Katarina:
                        mods = new[] { new StatModifier(StatKind.CriticalChance, def.Param(7) * mul, ModifierType.Flat) };
                        break;
                    case HeroIds.Tristana:
                        mods = new[] { new StatModifier(StatKind.CriticalDamage, def.Param(8) * mul, ModifierType.Flat) };
                        break;
                    case HeroIds.MissFortune:
                        var pen = def.Param(9) * mul;
                        mods = new[] {
                            new StatModifier(StatKind.MagicalPenetration, pen, ModifierType.Flat),
                            new StatModifier(StatKind.PhysicalPenetration, pen, ModifierType.Flat)
                        };
                        break;
                    default:
                        continue;
                }
                hero.AddModifierSet(ModifierSet.Permanent(hero, "mortal", mods, mark: false));
            }
        }
    }
}
