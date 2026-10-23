using System;
using System.Collections.Generic;

namespace AutoChess.Core.Combat.Heroes {
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

    /// <summary>Two slashes scaling with the target's missing health; the second always crits.</summary>
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

    /// <summary>
    /// Three slashes along a line through the target (and one cell behind it), knocking up; enemies below the health
    /// threshold get their armor shredded, extra true damage and a possible crit.
    /// </summary>
    public sealed class AatroxDarkSkill : SkillBehaviour {
        public const string SlashVisual = "aatrox_dark_slash";
        readonly double baseDmg;
        readonly double[] dmgMul;
        readonly double[] airborne;
        readonly double armorReduceMul;
        readonly double armorReduceDuration;
        readonly double hpThreshold;
        readonly double baseTrueDmg;
        readonly double trueDmgMul;
        readonly string effectKey;
        readonly List<CombatUnit> hits = new();

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
            AatroxLine.Collect(self, target, hits);
            self.Battle.PlayVisual(SlashVisual, VisualMode.Beam, self.Position, AatroxLine.End(self, target), 0.45);
            foreach (var u in hits) {
                if (!u.Alive) continue;
                var low = u.HpPercentage < hpThreshold;
                switch (index) {
                    case 0:
                        u.TakeDamage(self.MakeDamage(DamageType.Physical, false, baseDmg, dmgMul[0]), self);
                        if (low && u.Alive) {
                            u.AddModifierSet(ModifierSet.Timed(self, effectKey, armorReduceDuration,
                                new[] { new StatModifier(StatKind.Armor, armorReduceMul, ModifierType.Percent) }));
                        }
                        break;
                    case 1:
                        if (!low) u.TakeDamage(self.MakeDamage(DamageType.Physical, false, baseDmg, dmgMul[1]), self);
                        else {
                            u.TakeDamage(self.MakeDamage(DamageType.Physical, false, baseDmg, dmgMul[1]),
                                self.MakeDamage(DamageType.True, false, baseTrueDmg, trueDmgMul), self);
                        }
                        break;
                    default:
                        u.TakeDamage(self.MakeDamage(DamageType.Physical, low && self.RollCrit(), baseDmg, dmgMul[2]), self);
                        break;
                }
                u.Airborne(airborne[index]);
            }
        }
    }

    /// <summary>Aatrox's skills cut through the target and one cell behind it.</summary>
    static class AatroxLine {
        public static void Collect(CombatUnit self, CombatUnit target, List<CombatUnit> into) =>
            HeroLines.ThroughTarget(self, target, 1, into);

        public static Vec2 End(CombatUnit self, CombatUnit target) {
            var dx = target.Position.X - self.Position.X;
            var dy = target.Position.Y - self.Position.Y;
            var norm = Math.Sqrt(dx * dx + dy * dy);
            if (norm < 1e-9) return target.Position;
            return target.Position + new Vec2(dx / norm, dy / norm) * self.Battle.Grid.AverageCellDistance;
        }
    }

    /// <summary>Magic attacks growing on the same target (reset when it changes). Params 12: bonus per hit, 13: max hits.</summary>
    public sealed class AatroxLightAttack : AttackBehaviour {
        readonly double bonusPerHit;
        readonly int maxStacks;
        CombatUnit last;
        int stacks;

        public AatroxLightAttack(UnitDefinition def) {
            AnimationLength = 1.367;
            HitTimes = new[] { 0.18 };
            bonusPerHit = def.Param(12);
            maxStacks = (int)def.Param(13);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            if (target == last) stacks = Math.Min(maxStacks, stacks + 1);
            else {
                last = target;
                stacks = 0;
            }
            var damage = self.BasicDamage(DamageType.Magical);
            damage.Value *= 1 + stacks * bonusPerHit;
            var dealt = target.TakeDamage(damage, self);
            self.StealLife(dealt);
            self.RegenEnergy(self.Def.EnergyRegenPerAttack);
        }
    }

    /// <summary>
    /// Three magic slashes along a line through the target (and one cell behind it), knocking up and healing for a
    /// share of the damage (more when low).
    /// </summary>
    public sealed class AatroxLightSkill : SkillBehaviour {
        public const string SlashVisual = "aatrox_light_slash";
        readonly double baseDmg;
        readonly double[] dmgMul;
        readonly double[] airborne;
        readonly double[] vamp;
        readonly double vampMulOnLowHp;
        readonly double hpThreshold;
        readonly List<CombatUnit> hits = new();

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
            AatroxLine.Collect(self, target, hits);
            self.Battle.PlayVisual(SlashVisual, VisualMode.Beam, self.Position, AatroxLine.End(self, target), 0.45);
            var dealt = 0d;
            foreach (var u in hits) {
                if (!u.Alive) continue;
                dealt += u.TakeDamage(self.MakeDamage(DamageType.Magical, false, baseDmg, magicalRatio: dmgMul[index]), self);
                u.Airborne(airborne[index]);
            }
            var ratio = self.HpPercentage < hpThreshold ? vamp[index] * vampMulOnLowHp : vamp[index];
            self.Heal(dealt * ratio);
        }
    }
}
