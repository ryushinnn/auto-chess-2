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

    /// <summary>Every few attacks on the same target detonate plasma: bonus magic damage from its missing health.</summary>
    public sealed class KaisaAttack : AttackBehaviour {
        readonly int plasmaHits;
        readonly double plasmaBase;
        readonly double plasmaMissingHp;
        readonly string plasmaKey;

        public KaisaAttack(UnitDefinition def) {
            AnimationLength = 1.167;
            HitTimes = new[] { 0.35 };
            plasmaHits = Math.Max(1, (int)def.Param(0));
            plasmaBase = def.Param(1);
            plasmaMissingHp = def.Param(2);
            plasmaKey = def.SpecialKey(0);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            var stacks = target.MarkStacks(plasmaKey, self) + 1;
            double dealt;
            if (stacks >= plasmaHits) {
                var mark = target.GetMark(plasmaKey, self);
                if (mark != null) target.RemoveMark(mark);
                var plasma = new Damage(plasmaBase + (target.MaxHp - target.Hp) * plasmaMissingHp, DamageType.Magical, self.MagicalPenetration);
                dealt = target.TakeDamage(self.BasicDamage(DamageType.Physical), plasma, self);
            }
            else {
                dealt = target.TakeDamage(self.BasicDamage(DamageType.Physical), self);
                if (target.Alive) target.AddMark(new Mark { Owner = self, Key = plasmaKey, Stacks = stacks, Duration = double.PositiveInfinity });
            }
            self.StealLife(dealt);
            self.RegenEnergy(self.Def.EnergyRegenPerAttack);
        }
    }

    /// <summary>Missiles split among the enemies nearest the target; their number grows with attack speed.</summary>
    public sealed class KaisaSkill : SkillBehaviour {
        public const string MissileVisual = "kaisa_missile";
        const double FirstMissile = 0.5;
        const double LastMissile = 1.5;
        readonly double baseMissiles;
        readonly double missilesPerAttackSpeed;
        readonly int maxTargets;
        readonly double baseDmg;
        readonly double dmgMul;
        readonly List<CombatUnit> targets = new();

        public KaisaSkill(UnitDefinition def) {
            AnimationLength = 1.967;
            baseMissiles = def.Param(3);
            missilesPerAttackSpeed = def.Param(4);
            maxTargets = Math.Max(1, (int)def.Param(5));
            baseDmg = def.Param(6);
            dmgMul = def.Param(7);
        }

        public override void Begin(CombatUnit self, out double duration) {
            var missiles = Math.Clamp((int)Math.Floor(baseMissiles + missilesPerAttackSpeed * self.AttackSpeed + 1e-9), 1, 40);
            var times = new double[missiles];
            for (var i = 0; i < missiles; i++) times[i] = FirstMissile + (missiles == 1 ? 0 : i * (LastMissile - FirstMissile) / (missiles - 1));
            HitTimes = times;
            targets.Clear();
            var center = self.Target;
            if (center != null) {
                foreach (var o in self.Battle.AliveUnits(self.Side.Opponent())) {
                    var d = Vec2.SqrDistance(o.Position, center.Position);
                    var at = 0;
                    while (at < targets.Count && Vec2.SqrDistance(targets[at].Position, center.Position) <= d) at++;
                    targets.Insert(at, o);
                }
                if (targets.Count > maxTargets) targets.RemoveRange(maxTargets, targets.Count - maxTargets);
            }
            base.Begin(self, out duration);
        }

        public override void OnHit(CombatUnit self, int index) {
            for (var tries = 0; tries < targets.Count; tries++) {
                var target = targets[(index + tries) % targets.Count];
                if (!target.Alive) continue;
                self.Battle.PlayVisual(MissileVisual, VisualMode.Travel, self.Position, target.Position, 0.25);
                target.TakeDamage(self.MakeDamage(DamageType.Physical, self.RollCrit(), baseDmg, dmgMul), self, false);
                return;
            }
        }
    }
}
