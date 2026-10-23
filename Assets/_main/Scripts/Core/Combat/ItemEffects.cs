using System;
using System.Collections.Generic;

namespace AutoChess.Core.Combat {
    /// <summary>Passive of a combined item, set up once per copy on its holder when the battle starts.</summary>
    public abstract class ItemEffect {
        public abstract void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def);

        /// <summary>Runs <paramref name="action"/> every <paramref name="interval"/> seconds while the holder lives.</summary>
        protected static void Every(BattleSimulation battle, CombatUnit holder, double interval, Action action) {
            if (interval <= 0) return;
            var timer = 0d;
            battle.AddTicker(dt => {
                if (!holder.Alive) return;
                timer += dt;
                while (timer >= interval && holder.Alive) {
                    timer -= interval;
                    action();
                }
            });
        }

        /// <summary>Keeps <paramref name="modifiers"/> on the enemies within <paramref name="radius"/> cells of the holder.</summary>
        protected static void EnemyAura(BattleSimulation battle, CombatUnit holder, string key, int radius, StatModifier[] modifiers) {
            var inside = new List<CombatUnit>();
            Every(battle, holder, 0.5, () => {
                battle.UnitsInRadius(holder.Side.Opponent(), holder.Position, radius, inside);
                foreach (var u in inside) u.AddModifierSet(ModifierSet.Timed(holder, key, 0.75, modifiers, mark: false));
            });
        }

        /// <summary>Damage from the item itself (no on-hit / on-attack passives triggered by it).</summary>
        protected static double Strike(BattleSimulation battle, CombatUnit target, CombatUnit holder, Damage damage) {
            using (battle.Hit(HitKind.Effect)) return target.TakeDamage(damage, holder, false);
        }

        /// <summary>Holder's own damage, magic or physical, whichever it deals more of.</summary>
        protected static DamageType MainType(CombatUnit holder) =>
            holder.MagicalDamage > holder.PhysicalDamage ? DamageType.Magical : DamageType.Physical;
    }

    /// <summary>Item id → passive. Components have none.</summary>
    public static class ItemEffects {
        static readonly Dictionary<string, Func<ItemEffect>> Factories = new(StringComparer.Ordinal) {
            { "armor+armor", () => new ThornVest() },
            { "armor+atkspd", () => new TitansResolve() },
            { "armor+crit", () => new Executioner() },
            { "armor+energy", () => new FrozenHeart() },
            { "armor+maxhp", () => new Sunfire() },
            { "armor+mdmg", () => new Crownguard() },
            { "armor+pdmg", () => new EdgeOfNight() },
            { "armor+resistance", () => new Gargoyle() },
            { "atkspd+atkspd", () => new Longbow() },
            { "atkspd+crit", () => new LastWhisper() },
            { "atkspd+energy", () => new Statikk() },
            { "atkspd+maxhp", () => new Nashor() },
            { "atkspd+mdmg", () => new Rageblade() },
            { "atkspd+pdmg", () => new GiantSlayer() },
            { "atkspd+resistance", () => new Hurricane() },
            { "crit+crit", () => new Frenzy() },
            { "crit+energy", () => new HandOfJustice() },
            { "crit+maxhp", () => new Steadfast() },
            { "crit+mdmg", () => new JeweledGauntlet() },
            { "crit+pdmg", () => new InfinityEdge() },
            { "crit+resistance", () => new Quicksilver() },
            { "energy+energy", () => new BlueBuff() },
            { "energy+maxhp", () => new Redemption() },
            { "energy+mdmg", () => new Archangel() },
            { "energy+pdmg", () => new Shojin() },
            { "energy+resistance", () => new AdaptiveHelm() },
            { "maxhp+maxhp", () => new Warmog() },
            { "maxhp+mdmg", () => new Morello() },
            { "maxhp+pdmg", () => new Sterak() },
            { "maxhp+resistance", () => new Evenshroud() },
            { "mdmg+mdmg", () => new DamageAmp() },
            { "mdmg+pdmg", () => new Gunblade() },
            { "mdmg+resistance", () => new IonicSpark() },
            { "pdmg+pdmg", () => new DamageAmp() },
            { "pdmg+resistance", () => new Bloodthirster() },
            { "resistance+resistance", () => new DragonClaw() },
        };

        public static bool HasPassive(string itemId) => Factories.ContainsKey(itemId);

        /// <summary>Sets up the passives of the items of a side's living units (every copy counts).</summary>
        public static void Apply(BattleSimulation battle, TeamSide side) {
            var units = battle.AliveUnits(side);
            for (var i = 0; i < units.Count; i++) {
                var unit = units[i];
                foreach (var itemId in unit.Items) {
                    if (Factories.TryGetValue(itemId, out var factory)) factory().Apply(battle, unit, battle.Db.Item(itemId));
                }
            }
        }
    }

    // ---------------------------------------------------------------------- armor

    /// <summary>Crits against the holder deal normal damage; attacks on it burn the attacker. [0] damage, [1] cooldown.</summary>
    sealed class ThornVest : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            var ready = 0d;
            battle.AddHitHook(new HitHook(
                before: (CombatUnit target, CombatUnit source, ref Damage damage, HitKind kind) => {
                    if (target == holder && damage.Crit && source != null && source.CriticalDamage > 1) {
                        damage.Value /= source.CriticalDamage;
                        damage.Crit = false;
                    }
                    return true;
                },
                after: (target, source, damage, dealt, kind) => {
                    if (target != holder || kind != HitKind.Attack || source == null || !source.Alive || battle.Time < ready) return;
                    ready = battle.Time + def.Param(1);
                    Strike(battle, source, holder, new Damage(def.Param(0), DamageType.Magical, 0));
                }));
        }
    }

    /// <summary>Attacking or being hit stacks damage dealt; at full stacks armor and resistance. [0] per stack, [1] max, [2] defenses.</summary>
    sealed class TitansResolve : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            var stacks = 0;
            var max = (int)def.Param(1);
            battle.AddHitHook(new HitHook(
                before: (CombatUnit target, CombatUnit source, ref Damage damage, HitKind kind) => {
                    if (source == holder && stacks > 0) damage.Value *= 1 + stacks * def.Param(0);
                    return true;
                },
                after: (target, source, damage, dealt, kind) => {
                    if (stacks >= max || !holder.Alive) return;
                    if (target != holder && !(source == holder && kind == HitKind.Attack)) return;
                    stacks++;
                    if (stacks < max) return;
                    holder.AddModifierSet(ModifierSet.Permanent(holder, "titans_resolve", new[] {
                        new StatModifier(StatKind.Armor, def.Param(2), ModifierType.Flat),
                        new StatModifier(StatKind.Resistance, def.Param(2), ModifierType.Flat),
                    }, mark: false));
                }));
        }
    }

    /// <summary>Takedowns grant a shield. [0] shield (max health), [1] duration.</summary>
    sealed class Executioner : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            battle.UnitDied += (dead, killer) => {
                if (killer == holder && holder.Alive) holder.AddShield(holder, "item_executioner", holder.MaxHp * def.Param(0), def.Param(1));
            };
        }
    }

    /// <summary>Nearby enemies attack slower. [0] radius, [1] attack speed reduction.</summary>
    sealed class FrozenHeart : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) =>
            EnemyAura(battle, holder, "frozen_heart", (int)def.Param(0),
                new[] { new StatModifier(StatKind.AttackSpeed, -def.Param(1), ModifierType.Percent) });
    }

    /// <summary>Burns nearby enemies for a share of their max health and cuts their healing. [0] interval, [1] radius, [2] max health, [3] anti-heal.</summary>
    sealed class Sunfire : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            var inside = new List<CombatUnit>();
            Every(battle, holder, def.Param(0), () => {
                battle.UnitsInRadius(holder.Side.Opponent(), holder.Position, (int)def.Param(1), inside);
                foreach (var u in inside) {
                    Strike(battle, u, holder, new Damage(u.MaxHp * def.Param(2), DamageType.Magical, 0));
                    u.AntiHeal(def.Param(3));
                }
            });
        }
    }

    /// <summary>A shield when the battle starts. [0] shield (max health), [1] duration.</summary>
    sealed class Crownguard : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) =>
            holder.AddShield(holder, "item_crownguard", holder.MaxHp * def.Param(0), def.Param(1));
    }

    /// <summary>Once below a health share: invulnerable for a moment, then faster attacks. [0] threshold, [1] invulnerability, [2] attack speed, [3] duration.</summary>
    sealed class EdgeOfNight : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            var used = false;
            battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (used || target != holder || !holder.Alive || holder.HpPercentage >= def.Param(0)) return;
                used = true;
                holder.MakeInvulnerable(def.Param(1));
                holder.AddModifierSet(ModifierSet.Timed(holder, "edge_of_night", def.Param(1) + def.Param(3), new[] {
                    new StatModifier(StatKind.AttackSpeed, def.Param(2), ModifierType.Percent),
                }, mark: false));
            }));
        }
    }

    /// <summary>Armor and resistance for every enemy targeting the holder. [0] per enemy, [1] max enemies.</summary>
    sealed class Gargoyle : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            var shown = -1;
            Every(battle, holder, 0.5, () => {
                var count = 0;
                foreach (var u in battle.AliveUnits(holder.Side.Opponent())) {
                    if (u.Target == holder) count++;
                }
                count = Math.Min(count, (int)def.Param(1));
                if (count == shown) return;
                shown = count;
                var bonus = count * def.Param(0);
                holder.AddModifierSet(ModifierSet.Permanent(holder, "gargoyle", new[] {
                    new StatModifier(StatKind.Armor, bonus, ModifierType.Flat),
                    new StatModifier(StatKind.Resistance, bonus, ModifierType.Flat),
                }, mark: false));
            });
        }
    }

    // ---------------------------------------------------------------------- attack speed

    /// <summary>Longer attack range. [0] cells.</summary>
    sealed class Longbow : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) => holder.BonusRange += (int)def.Param(0);
    }

    /// <summary>Critical attacks shred armor. [0] armor reduction, [1] duration.</summary>
    sealed class LastWhisper : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (source != holder || kind != HitKind.Attack || !damage.Crit || !target.Alive) return;
                target.AddModifierSet(ModifierSet.Timed(holder, "last_whisper", def.Param(1), new[] {
                    new StatModifier(StatKind.Armor, -def.Param(0), ModifierType.Percent),
                }, mark: false));
            }));
        }
    }

    /// <summary>Every few attacks, lightning hits the enemies nearest the target. [0] attacks, [1] damage, [2] targets.</summary>
    sealed class Statikk : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            var count = 0;
            var near = new List<CombatUnit>();
            battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (source != holder || kind != HitKind.Attack) return;
                if (++count < (int)def.Param(0)) return;
                count = 0;
                near.Clear();
                foreach (var o in battle.AliveUnits(holder.Side.Opponent())) {
                    var d = Vec2.SqrDistance(o.Position, target.Position);
                    var at = 0;
                    while (at < near.Count && Vec2.SqrDistance(near[at].Position, target.Position) <= d) at++;
                    near.Insert(at, o);
                }
                var hits = Math.Min(near.Count, (int)def.Param(2));
                for (var i = 0; i < hits; i++) Strike(battle, near[i], holder, new Damage(def.Param(1), DamageType.Magical, holder.MagicalPenetration));
            }));
        }
    }

    /// <summary>After casting, faster attacks for a while. [0] attack speed, [1] duration.</summary>
    sealed class Nashor : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            battle.SkillCompleted += u => {
                if (u != holder || !holder.Alive) return;
                holder.AddModifierSet(ModifierSet.Timed(holder, "nashor", def.Param(1), new[] {
                    new StatModifier(StatKind.AttackSpeed, def.Param(0), ModifierType.Percent),
                }, mark: false));
            };
        }
    }

    /// <summary>Each attack adds attack speed for the rest of the battle. [0] per attack.</summary>
    sealed class Rageblade : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            var stacks = 0;
            battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (source != holder || kind != HitKind.Attack || !holder.Alive) return;
                stacks++;
                holder.AddModifierSet(ModifierSet.Permanent(holder, "rageblade", new[] {
                    new StatModifier(StatKind.AttackSpeed, stacks * def.Param(0), ModifierType.Percent),
                }, mark: false));
            }));
        }
    }

    /// <summary>More damage to enemies with more max health than the holder. [0] bonus.</summary>
    sealed class GiantSlayer : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            battle.AddHitHook(new HitHook(before: (CombatUnit target, CombatUnit source, ref Damage damage, HitKind kind) => {
                if (source == holder && target.MaxHp > holder.MaxHp) damage.Value *= 1 + def.Param(0);
                return true;
            }));
        }
    }

    /// <summary>Attacks also hit the enemy nearest the target for part of the damage. [0] share.</summary>
    sealed class Hurricane : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (source != holder || kind != HitKind.Attack) return;
                CombatUnit best = null;
                var bestSqr = double.MaxValue;
                foreach (var o in battle.AliveUnits(holder.Side.Opponent())) {
                    if (o == target) continue;
                    var d = Vec2.SqrDistance(o.Position, target.Position);
                    if (d < bestSqr) {
                        bestSqr = d;
                        best = o;
                    }
                }
                if (best != null) Strike(battle, best, holder, new Damage(damage.Value * def.Param(0), damage.Type, damage.Penetration, damage.Crit));
            }));
        }
    }

    // ---------------------------------------------------------------------- crit

    /// <summary>Every crit adds attack speed. [0] per crit, [1] max stacks.</summary>
    sealed class Frenzy : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            var stacks = 0;
            battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (source != holder || !damage.Crit || stacks >= (int)def.Param(1) || !holder.Alive) return;
                stacks++;
                holder.AddModifierSet(ModifierSet.Permanent(holder, "frenzy", new[] {
                    new StatModifier(StatKind.AttackSpeed, stacks * def.Param(0), ModifierType.Percent),
                }, mark: false));
            }));
        }
    }

    /// <summary>Heals from all damage dealt, twice as much at low health. [0] share, [1] health threshold.</summary>
    sealed class HandOfJustice : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (source != holder || dealt <= 0 || !holder.Alive) return;
                holder.Heal(dealt * def.Param(0) * (holder.HpPercentage < def.Param(1) ? 2 : 1));
            }));
        }
    }

    /// <summary>Takes less damage while healthy. [0] reduction, [1] health threshold.</summary>
    sealed class Steadfast : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            battle.AddHitHook(new HitHook(before: (CombatUnit target, CombatUnit source, ref Damage damage, HitKind kind) => {
                if (target == holder && holder.HpPercentage > def.Param(1)) damage.Value *= 1 - def.Param(0);
                return true;
            }));
        }
    }

    /// <summary>Skills can crit; takedowns restore energy. [0] energy.</summary>
    sealed class JeweledGauntlet : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            battle.AddHitHook(new HitHook(before: (CombatUnit target, CombatUnit source, ref Damage damage, HitKind kind) => {
                if (source != holder || kind != HitKind.Ability || damage.Crit || damage.Type == DamageType.True) return true;
                if (holder.RollCrit()) {
                    damage.Value *= holder.CriticalDamage;
                    damage.Crit = true;
                }
                return true;
            }));
            battle.UnitDied += (dead, killer) => {
                if (killer == holder && holder.Alive) holder.RegenEnergy(def.Param(0));
            };
        }
    }

    /// <summary>Critical attacks add true damage. [0] share of the hit.</summary>
    sealed class InfinityEdge : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (source != holder || kind != HitKind.Attack || !damage.Crit || !target.Alive || dealt <= 0) return;
                Strike(battle, target, holder, new Damage(dealt * def.Param(0), DamageType.True, 0));
            }));
        }
    }

    /// <summary>Immune to crowd control at the start; attack speed every second meanwhile. [0] duration, [1] attack speed per second.</summary>
    sealed class Quicksilver : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            var duration = def.Param(0);
            holder.Status.CrowdControlImmune = Math.Max(holder.Status.CrowdControlImmune, duration);
            var stacks = 0;
            Every(battle, holder, 1, () => {
                if (battle.Time > duration + 1e-9) return;
                stacks++;
                holder.AddModifierSet(ModifierSet.Permanent(holder, "quicksilver", new[] {
                    new StatModifier(StatKind.AttackSpeed, stacks * def.Param(1), ModifierType.Percent),
                }, mark: false));
            });
        }
    }

    // ---------------------------------------------------------------------- energy

    /// <summary>Energy right after casting. [0] energy.</summary>
    sealed class BlueBuff : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            battle.SkillCompleted += u => {
                if (u == holder && holder.Alive) holder.RegenEnergy(def.Param(0));
            };
        }
    }

    /// <summary>Heals nearby allies for part of their missing health. [0] interval, [1] missing health, [2] radius.</summary>
    sealed class Redemption : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            var allies = new List<CombatUnit>();
            Every(battle, holder, def.Param(0), () => {
                battle.UnitsInRadius(holder.Side, holder.Position, (int)def.Param(2), allies);
                foreach (var u in allies) {
                    var missing = u.MaxHp - u.Hp;
                    if (missing > 1) u.Heal(missing * def.Param(1));
                }
            });
        }
    }

    /// <summary>Magic damage growing over the battle. [0] interval, [1] magic damage per stack.</summary>
    sealed class Archangel : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            var stacks = 0;
            Every(battle, holder, def.Param(0), () => {
                stacks++;
                holder.AddModifierSet(ModifierSet.Permanent(holder, "archangel", new[] {
                    new StatModifier(StatKind.MagicalDamage, stacks * def.Param(1), ModifierType.Percent),
                }, mark: false));
            });
        }
    }

    /// <summary>Attacks restore extra energy. [0] energy.</summary>
    sealed class Shojin : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (source == holder && kind == HitKind.Attack && holder.Alive) holder.RegenEnergy(def.Param(0));
            }));
        }
    }

    /// <summary>Energy at the start and over time. [0] starting energy, [1] interval, [2] energy.</summary>
    sealed class AdaptiveHelm : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            holder.RegenEnergy(def.Param(0));
            Every(battle, holder, def.Param(1), () => holder.RegenEnergy(def.Param(2)));
        }
    }

    // ---------------------------------------------------------------------- health

    /// <summary>Regenerates part of its missing health every second. [0] missing health.</summary>
    sealed class Warmog : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            Every(battle, holder, 1, () => {
                var missing = holder.MaxHp - holder.Hp;
                if (missing > 1) holder.Heal(missing * def.Param(0));
            });
        }
    }

    /// <summary>Skill damage burns (true damage from max health) and cuts healing. [0] max health per second, [1] duration.</summary>
    sealed class Morello : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (source != holder || kind != HitKind.Ability || !target.Alive) return;
                var seconds = Math.Max(1, (int)def.Param(1));
                target.AntiHeal(seconds);
                target.AddDamageOverTime(DamageOverTime.Create("morello", holder,
                    new Damage(target.MaxHp * def.Param(0), DamageType.True, 0), seconds, 1, applyInstantly: false, mark: false));
            }));
        }
    }

    /// <summary>Once below a health share: shield and physical damage. [0] threshold, [1] shield (max health), [2] physical damage, [3] duration.</summary>
    sealed class Sterak : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            var used = false;
            battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (used || target != holder || !holder.Alive || holder.HpPercentage >= def.Param(0)) return;
                used = true;
                holder.AddShield(holder, "item_sterak", holder.MaxHp * def.Param(1), def.Param(3));
                holder.AddModifierSet(ModifierSet.Timed(holder, "sterak", def.Param(3), new[] {
                    new StatModifier(StatKind.PhysicalDamage, def.Param(2), ModifierType.Percent),
                }, mark: false));
            }));
        }
    }

    /// <summary>Nearby enemies lose armor. [0] radius, [1] armor reduction.</summary>
    sealed class Evenshroud : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) =>
            EnemyAura(battle, holder, "evenshroud", (int)def.Param(0),
                new[] { new StatModifier(StatKind.Armor, -def.Param(1), ModifierType.Percent) });
    }

    // ---------------------------------------------------------------------- damage

    /// <summary>More damage dealt. [0] bonus.</summary>
    sealed class DamageAmp : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            battle.AddHitHook(new HitHook(before: (CombatUnit target, CombatUnit source, ref Damage damage, HitKind kind) => {
                if (source == holder) damage.Value *= 1 + def.Param(0);
                return true;
            }));
        }
    }

    /// <summary>Heals the weakest ally for part of all damage dealt. [0] share.</summary>
    sealed class Gunblade : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (source != holder || dealt <= 0 || !holder.Alive) return;
                battle.WeakestAlly(holder)?.Heal(dealt * def.Param(0));
            }));
        }
    }

    /// <summary>Nearby enemies lose magic resistance. [0] radius, [1] resistance reduction.</summary>
    sealed class IonicSpark : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) =>
            EnemyAura(battle, holder, "ionic_spark", (int)def.Param(0),
                new[] { new StatModifier(StatKind.Resistance, -def.Param(1), ModifierType.Percent) });
    }

    /// <summary>Once below a health share: a shield. [0] threshold, [1] shield (max health), [2] duration.</summary>
    sealed class Bloodthirster : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) {
            var used = false;
            battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (used || target != holder || !holder.Alive || holder.HpPercentage >= def.Param(0)) return;
                used = true;
                holder.AddShield(holder, "item_bloodthirster", holder.MaxHp * def.Param(1), def.Param(2));
            }));
        }
    }

    /// <summary>Heals a share of max health every few seconds. [0] interval, [1] max health.</summary>
    sealed class DragonClaw : ItemEffect {
        public override void Apply(BattleSimulation battle, CombatUnit holder, ItemDefinition def) =>
            Every(battle, holder, def.Param(0), () => holder.Heal(holder.MaxHp * def.Param(1)));
    }
}
