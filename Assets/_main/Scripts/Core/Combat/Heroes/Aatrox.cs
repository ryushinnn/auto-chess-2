using System;

namespace AutoChess.Core.Combat.Heroes {
    /// <summary>Two slashes scaling with the target's missing health; the second always crits.</summary>
    /// <summary>
    /// The Apocalypse Warrior (Chaos synergy): every swing hits with both its physical and its magic damage
    /// (it sums both Aatrox halves), with lifesteal and one crit roll.
    /// </summary>
    public sealed class AatroxTrueFormAttack : AttackBehaviour {
        public AatroxTrueFormAttack() {
            AnimationLength = 1.367;
            HitTimes = new[] { 0.18 };
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            var crit = self.RollCrit();
            var dealt = target.TakeDamage(self.MakeDamage(DamageType.Physical, crit, physicalRatio: 1),
                self.MakeDamage(DamageType.Magical, crit, magicalRatio: 1), self);
            self.StealLife(dealt);
        }
    }

    public sealed class AatroxDarkAttack : AttackBehaviour {
        readonly double dmgMulMin;
        readonly double dmgMulMax;

        public AatroxDarkAttack(UnitDefinition def) {
            AnimationLength = 2.167;
            HitTimes = new[] { 0.9, 1.9 };
            dmgMulMin = def.Param(0);
            dmgMulMax = def.Param(1);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target != null) {
                var ratio = Lerp(dmgMulMin, dmgMulMax, target.HpLostPercentage);
                var dealt = target.TakeDamage(self.MakeDamage(DamageType.Physical, index == 1, physicalRatio: ratio), self);
                self.StealLife(dealt);
            }
            if (index == 1) self.RegenEnergy(self.Def.EnergyRegenPerAttack);
        }

        static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);
    }

    /// <summary>Three slashes knocking up; below the health threshold they shred armor and add true damage.</summary>
    public sealed class AatroxDarkSkill : SkillBehaviour {
        readonly double baseDmg;
        readonly double[] dmgMul;
        readonly double[] airborne;
        readonly double armorReduceMul;
        readonly double armorReduceDuration;
        readonly double hpThreshold;
        readonly double baseTrueDmg;
        readonly double trueDmgMul;
        readonly string effectKey;

        public AatroxDarkSkill(UnitDefinition def) {
            AnimationLength = 5;
            HitTimes = new[] { 0.6, 2.3, 4.1 };
            baseDmg = def.Param(2);
            dmgMul = new[] { def.Param(3), def.Param(4), def.Param(5) };
            airborne = new[] { def.Param(6), def.Param(7), def.Param(8) };
            armorReduceMul = def.Param(9);
            armorReduceDuration = def.Param(10);
            hpThreshold = def.Param(11);
            baseTrueDmg = def.Param(12);
            trueDmgMul = def.Param(13);
            effectKey = def.SpecialKey(0);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            switch (index) {
                case 0:
                    target.TakeDamage(self.MakeDamage(DamageType.Physical, false, baseDmg, dmgMul[0]), self);
                    if (target.HpPercentage < hpThreshold) {
                        target.AddModifierSet(ModifierSet.Timed(self, effectKey, armorReduceDuration,
                            new[] { new StatModifier(StatKind.Armor, armorReduceMul, ModifierType.Percent) }));
                    }
                    break;
                case 1:
                    if (target.HpPercentage >= hpThreshold) {
                        target.TakeDamage(self.MakeDamage(DamageType.Physical, false, baseDmg, dmgMul[1]), self);
                    }
                    else {
                        target.TakeDamage(
                            self.MakeDamage(DamageType.Physical, false, baseDmg, dmgMul[1]),
                            self.MakeDamage(DamageType.True, false, baseTrueDmg, trueDmgMul), self);
                    }
                    break;
                default:
                    target.TakeDamage(self.MakeDamage(DamageType.Physical, self.RollCrit(), baseDmg, dmgMul[2]), self);
                    break;
            }
            target.Airborne(airborne[index]);
        }
    }

    /// <summary>Three magic slashes knocking up and healing for a share of the damage (more when low).</summary>
    public sealed class AatroxLightSkill : SkillBehaviour {
        readonly double baseDmg;
        readonly double[] dmgMul;
        readonly double[] airborne;
        readonly double[] vamp;
        readonly double vampMulOnLowHp;
        readonly double hpThreshold;

        public AatroxLightSkill(UnitDefinition def) {
            AnimationLength = 5;
            HitTimes = new[] { 0.6, 2.3, 4.1 };
            // Param order of the asset: base_dmg, dmg_mul_0..2, airborne_0..2, vamp_0..2, vamp_mul_on_low_hp,
            // hp_threshold (the old code read them one slot off: an 80x first slash).
            baseDmg = def.Param(0);
            dmgMul = new[] { def.Param(1), def.Param(2), def.Param(3) };
            airborne = new[] { def.Param(4), def.Param(5), def.Param(6) };
            vamp = new[] { def.Param(7), def.Param(8), def.Param(9) };
            vampMulOnLowHp = def.Param(10);
            hpThreshold = def.Param(11);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            var dealt = target.TakeDamage(self.MakeDamage(DamageType.Magical, false, baseDmg, magicalRatio: dmgMul[index]), self);
            var ratio = self.HpPercentage < hpThreshold ? vamp[index] * vampMulOnLowHp : vamp[index];
            self.Heal(dealt * ratio);
            target.Airborne(airborne[index]);
        }
    }
}
