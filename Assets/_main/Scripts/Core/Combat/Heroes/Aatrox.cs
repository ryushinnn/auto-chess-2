using System;

namespace AutoChess.Core.Combat.Heroes {
    /// <summary>Two slashes scaling with the target's missing health; the second always crits.</summary>
    public sealed class AatroxDarkAttack : AttackBehaviour {
        readonly float dmgMulMin;
        readonly float dmgMulMax;

        public AatroxDarkAttack(UnitDefinition def) {
            AnimationLength = 2.167f;
            HitTimes = new[] { 0.9f, 1.9f };
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

        static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0, 1);
    }

    /// <summary>Three slashes knocking up; below the health threshold they shred armor and add true damage.</summary>
    public sealed class AatroxDarkSkill : SkillBehaviour {
        readonly float baseDmg;
        readonly float[] dmgMul;
        readonly float[] airborne;
        readonly float armorReduceMul;
        readonly float armorReduceDuration;
        readonly float hpThreshold;
        readonly float baseTrueDmg;
        readonly float trueDmgMul;
        readonly string effectKey;

        public AatroxDarkSkill(UnitDefinition def) {
            AnimationLength = 5;
            HitTimes = new[] { 0.6f, 2.3f, 4.1f };
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
        // The original processor never assigned its base damage, so it is always 0.
        const float BaseDmg = 0;
        readonly float[] dmgMul;
        readonly float[] airborne;
        readonly float[] vamp;
        readonly float vampMulOnLowHp;
        readonly float hpThreshold;

        public AatroxLightSkill(UnitDefinition def) {
            AnimationLength = 5;
            HitTimes = new[] { 0.6f, 2.3f, 4.1f };
            dmgMul = new[] { def.Param(0), def.Param(1), def.Param(2) };
            airborne = new[] { def.Param(3), def.Param(4), def.Param(5) };
            vamp = new[] { def.Param(6), def.Param(7), def.Param(8) };
            vampMulOnLowHp = def.Param(9);
            hpThreshold = def.Param(10);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            var dealt = target.TakeDamage(self.MakeDamage(DamageType.Magical, false, BaseDmg, magicalRatio: dmgMul[index]), self);
            var ratio = self.HpPercentage < hpThreshold ? vamp[index] * vampMulOnLowHp : vamp[index];
            self.Heal(dealt * ratio);
            target.Airborne(airborne[index]);
        }
    }
}
