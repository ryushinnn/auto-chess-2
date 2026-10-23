using System;
using System.Collections.Generic;

namespace AutoChess.Core.Combat.Heroes {
    /// <summary>Arrow projectile; damage, lifesteal and energy apply on impact.</summary>
    public sealed class AsheAttack : AttackBehaviour {
        public const string ProjectileVisual = "ashe_attack";

        public AsheAttack() {
            AnimationLength = 1d;
            HitTimes = new[] { 0.2 };
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            self.Battle.LaunchProjectile(self, target, ProjectileVisual, p => {
                var dealt = p.Target.TakeDamage(self.BasicDamage(DamageType.Physical), self);
                self.StealLife(dealt);
                self.RegenEnergy(self.Def.EnergyRegenPerAttack);
            });
        }
    }

    /// <summary>Rains a random number of arrow volleys on the target's area; every volley can crit.</summary>
    public sealed class AsheSkill : SkillBehaviour {
        public const string AreaVisual = "ashe_skill";
        readonly int setMin;
        readonly int setMax;
        readonly double baseDmg;
        readonly double pDmgMul;
        readonly double mDmgMul;
        readonly double totalTime;
        readonly double radius;

        public AsheSkill(UnitDefinition def) {
            AnimationLength = 3;
            HitTimes = new[] { 0.75 };
            setMin = (int)def.Param(0);
            setMax = (int)def.Param(1);
            baseDmg = def.Param(2);
            pDmgMul = def.Param(3);
            mDmgMul = def.Param(4);
            totalTime = def.Param(5);
            radius = def.Param(6);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            var damage = self.MakeDamage(DamageType.Physical, false, baseDmg, pDmgMul, mDmgMul);
            var sets = Math.Max(1, self.Battle.Rng.Range(setMin, setMax + 1));
            self.Battle.CreateArea(self, target.Position, radius, damage, sets, totalTime / sets, true, AreaVisual);
        }
    }

    /// <summary>Tea (heal over time) then focus (attack speed).</summary>
    public sealed class CaitlynSkill : SkillBehaviour {
        readonly double healByHpMul;
        readonly double healByDmgMul;
        readonly double duration;
        readonly double interval;
        readonly double atkSpdMul;
        readonly double atkSpdDuration;
        readonly string hotKey;
        readonly string effectKey;

        public CaitlynSkill(UnitDefinition def) {
            AnimationLength = 6.5;
            HitTimes = new[] { 2.2, 5.6 };
            healByHpMul = def.Param(0);
            healByDmgMul = def.Param(1);
            duration = def.Param(2);
            interval = def.Param(3);
            atkSpdMul = def.Param(4);
            atkSpdDuration = def.Param(5);
            hotKey = def.SpecialKey(0);
            effectKey = def.SpecialKey(1);
        }

        public override void OnHit(CombatUnit self, int index) {
            if (!self.Alive) return;
            if (index == 0) {
                self.AddHealOverTime(HealOverTime.Create(hotKey, self,
                    self.MaxHp * healByHpMul + self.PhysicalDamage * healByDmgMul,
                    (int)Math.Round(duration / interval), interval));
            }
            else {
                self.AddModifierSet(ModifierSet.Timed(self, effectKey, atkSpdDuration,
                    new[] { new StatModifier(StatKind.AttackSpeed, atkSpdMul, ModifierType.Percent) }));
            }
        }
    }

    /// <summary>A volley of rockets, each of a random damage type; energy is gained once per target.</summary>
    public sealed class JinxSkill : SkillBehaviour {
        readonly double baseDmgPerRocket;
        readonly double dmgMulPerRocket;
        readonly HashSet<int> affected = new();

        public JinxSkill(UnitDefinition def) {
            AnimationLength = 4.2;
            var rockets = Math.Max(1, (int)def.Param(0));
            // The old code truncated the interval to whole seconds (0.2 -> 0), firing every rocket at once.
            var interval = def.Param(1);
            var times = new double[rockets];
            for (var i = 0; i < rockets; i++) times[i] = 2d + i * interval;
            HitTimes = times;
            baseDmgPerRocket = def.Param(2);
            dmgMulPerRocket = def.Param(3);
        }

        public override void OnHit(CombatUnit self, int index) {
            if (index == 0) affected.Clear();
            var target = self.Target;
            if (target == null) return;
            var type = self.Battle.Rng.Range(0, 3) switch {
                0 => DamageType.Physical,
                1 => DamageType.Magical,
                _ => DamageType.True
            };
            var damage = self.MakeDamage(type, self.RollCrit(), baseDmgPerRocket, dmgMulPerRocket);
            target.TakeDamage(damage, self, affected.Add(target.Id));
        }
    }

    /// <summary>Attack: a physical and a magical part sharing one crit roll.</summary>
    public sealed class MissFortuneAttack : AttackBehaviour {
        readonly double pDmgMul0, mDmgMul0, pDmgMul1, mDmgMul1;

        public MissFortuneAttack(UnitDefinition def) {
            AnimationLength = 1.167;
            HitTimes = new[] { 0.35 };
            pDmgMul0 = def.Param(0);
            mDmgMul0 = def.Param(1);
            pDmgMul1 = def.Param(2);
            mDmgMul1 = def.Param(3);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            var crit = self.RollCrit();
            var dealt = target.TakeDamage(
                self.MakeDamage(DamageType.Physical, crit, 0, pDmgMul0, mDmgMul0),
                self.MakeDamage(DamageType.Magical, crit, 0, pDmgMul1, mDmgMul1), self);
            self.StealLife(dealt);
            self.RegenEnergy(self.Def.EnergyRegenPerAttack);
        }
    }

    /// <summary>Two shots: the first heals, the second shreds armor and resistance.</summary>
    public sealed class MissFortuneSkill : SkillBehaviour {
        readonly double lP0, lM0, lP1, lM1, rP0, rM0, rP1, rM1;
        readonly double vampMul;
        readonly double reduceDefMul;
        readonly double reduceDefDuration;
        readonly string effectKey;

        public MissFortuneSkill(UnitDefinition def) {
            AnimationLength = 2.8;
            HitTimes = new[] { 1d, 1.3 };
            lP0 = def.Param(0);
            lM0 = def.Param(1);
            lP1 = def.Param(2);
            lM1 = def.Param(3);
            rP0 = def.Param(4);
            rM0 = def.Param(5);
            rP1 = def.Param(6);
            rM1 = def.Param(7);
            vampMul = def.Param(8);
            reduceDefMul = def.Param(9);
            reduceDefDuration = def.Param(10);
            effectKey = def.SpecialKey(0);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            var crit = self.RollCrit();
            if (index == 0) {
                var dealt = target.TakeDamage(
                    self.MakeDamage(DamageType.Physical, crit, 0, lP0, lM0),
                    self.MakeDamage(DamageType.Magical, crit, 0, lP1, lM1), self);
                self.Heal(dealt * vampMul);
            }
            else {
                target.TakeDamage(
                    self.MakeDamage(DamageType.Physical, crit, 0, rP0, rM0),
                    self.MakeDamage(DamageType.Magical, crit, 0, rP1, rM1), self);
                target.AddModifierSet(ModifierSet.Timed(self, effectKey, reduceDefDuration, new[] {
                    new StatModifier(StatKind.Armor, reduceDefMul, ModifierType.Percent),
                    new StatModifier(StatKind.Resistance, reduceDefMul, ModifierType.Percent),
                }));
            }
        }
    }

    /// <summary>Attack speed and crit chance burst; the energy bar drains for the duration.</summary>
    public sealed class TristanaSkill : SkillBehaviour {
        readonly double atkSpdMul;
        readonly double critChanceMul;
        readonly double duration;
        readonly string effectKey;

        public TristanaSkill(UnitDefinition def) {
            AnimationLength = 2.2;
            HitTimes = new[] { 0.625 };
            atkSpdMul = def.Param(0);
            critChanceMul = def.Param(1);
            duration = def.Param(2);
            effectKey = def.SpecialKey(0);
            DrainsEnergy = true;
            DrainDelay = HitTimes[0];
            DrainDuration = duration;
        }

        public override void OnHit(CombatUnit self, int index) {
            if (!self.Alive) return;
            self.AddModifierSet(ModifierSet.Timed(self, effectKey, duration, new[] {
                new StatModifier(StatKind.AttackSpeed, atkSpdMul, ModifierType.Percent),
                new StatModifier(StatKind.CriticalChance, critChanceMul, ModifierType.Percent),
            }));
        }
    }
}
