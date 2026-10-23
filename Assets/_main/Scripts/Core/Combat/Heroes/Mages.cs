using System;

namespace AutoChess.Core.Combat.Heroes {
    /// <summary>Ignite stacks: true damage on hit (proportional to stacks) plus a burn over time.</summary>
    static class TeemoIgnite {
        public static Damage Make(CombatUnit self, CombatUnit target, int stacks, double dmgMulLimit, double dmgByMaxHp) =>
            new(stacks * Math.Min(self.MagicalDamage * dmgMulLimit, target.MaxHp * dmgByMaxHp), DamageType.True, 0);

        public static void Burn(CombatUnit self, CombatUnit target, string key, Damage ignite, int stacks, double duration,
            double interval) {
            // One tick is already dealt with the hit, hence "- 1".
            target.AddDamageOverTime(DamageOverTime.Create(key, self, ignite,
                (int)Math.Round(duration / interval) - 1, interval, stacks, false));
        }
    }

    public sealed class TeemoAttack : AttackBehaviour {
        readonly int maxStacks;
        readonly double duration;
        readonly double interval;
        readonly double dmgByMaxHp;
        readonly double dmgMulLimit;
        readonly string dotKey;

        public TeemoAttack(UnitDefinition def) {
            AnimationLength = 0.933;
            HitTimes = new[] { 0.17 };
            maxStacks = (int)def.Param(0);
            duration = def.Param(1);
            interval = def.Param(2);
            dmgByMaxHp = def.Param(3);
            dmgMulLimit = def.Param(4);
            dotKey = def.SpecialKey(0);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            var stacks = Math.Min(target.MarkStacks(dotKey, self) + 1, maxStacks);
            var ignite = TeemoIgnite.Make(self, target, stacks, dmgMulLimit, dmgByMaxHp);
            // The ignite part rides along for the stacked damage numbers but does not count as attack damage.
            var dealt = target.TakeDamage(self.BasicDamage(DamageType.Magical), ignite, self) - ignite.Value;
            self.StealLife(dealt);
            self.RegenEnergy(self.Def.EnergyRegenPerAttack);
            TeemoIgnite.Burn(self, target, dotKey, ignite, stacks, duration, interval);
        }
    }

    /// <summary>Three bombs, stronger per ignite stack; the last one crits.</summary>
    public sealed class TeemoSkill : SkillBehaviour {
        readonly int maxStacks;
        readonly double duration;
        readonly double interval;
        readonly double dmgByMaxHp;
        readonly double dmgMulLimit;
        readonly string dotKey;
        readonly double baseDmg;
        readonly double dmgMul;
        readonly double bonusBaseDmgPerStack;
        readonly double bonusDmgMulPerStack;

        public TeemoSkill(UnitDefinition def) {
            AnimationLength = 4;
            HitTimes = new[] { 1.56, 2.73, 3.29 };
            maxStacks = (int)def.Param(0);
            duration = def.Param(1);
            interval = def.Param(2);
            dmgByMaxHp = def.Param(3);
            dmgMulLimit = def.Param(4);
            baseDmg = def.Param(5);
            dmgMul = def.Param(6);
            bonusBaseDmgPerStack = def.Param(7);
            bonusDmgMulPerStack = def.Param(8);
            dotKey = def.SpecialKey(0);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            var current = target.MarkStacks(dotKey, self);
            var stacks = Math.Min(current + 1, maxStacks);
            var ignite = TeemoIgnite.Make(self, target, stacks, dmgMulLimit, dmgByMaxHp);
            var main = self.MakeDamage(DamageType.Magical, index == 2,
                baseDmg + bonusBaseDmgPerStack * current,
                magicalRatio: dmgMul + bonusDmgMulPerStack * current);
            target.TakeDamage(main, ignite, self);
            TeemoIgnite.Burn(self, target, dotKey, ignite, stacks, duration, interval);
        }
    }

    public enum YoneSword {
        Divine,
        Devil
    }

    /// <summary>Alternates swords: divine (physical, stacks weakening) and devil (magic, lifesteal on missing health).</summary>
    public sealed class YoneAttack : AttackBehaviour {
        readonly double dmgReduceMul;
        readonly int maxStacks;
        readonly double duration;
        readonly double vampMin;
        readonly double vampMax;
        readonly string effectKey;

        public YoneSword CurrentSword { get; private set; } = YoneSword.Divine;

        public YoneAttack(UnitDefinition def) {
            AnimationLength = 1.367;
            HitTimes = new[] { 0.5 };
            dmgReduceMul = def.Param(0);
            maxStacks = (int)def.Param(1);
            duration = def.Param(2);
            vampMin = def.Param(3);
            vampMax = def.Param(4);
            effectKey = def.SpecialKey(0);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target != null) {
                var divine = CurrentSword == YoneSword.Divine;
                var damage = divine
                    ? self.BasicDamage(DamageType.Physical)
                    : self.MakeDamage(DamageType.Magical, self.RollCrit(), physicalRatio: 1d);
                var dealt = target.TakeDamage(damage, self);
                var heal = dealt * self.LifeSteal;
                if (!divine) heal += dealt * (vampMin + (vampMax - vampMin) * Math.Clamp(self.HpLostPercentage, 0, 1));
                if (heal > 0) self.Heal(heal);

                if (divine) {
                    // The stack count is shown on the mark; the effect itself does not grow with it.
                    var stacks = Math.Min(target.MarkStacks(effectKey, self) + 1, maxStacks);
                    target.AddModifierSet(ModifierSet.Timed(self, effectKey, duration, new[] {
                        new StatModifier(StatKind.PhysicalDamage, dmgReduceMul, ModifierType.Percent),
                        new StatModifier(StatKind.MagicalDamage, dmgReduceMul, ModifierType.Percent),
                    }, stacks));
                }
                self.RegenEnergy(self.Def.EnergyRegenPerAttack);
            }
            CurrentSword = CurrentSword == YoneSword.Divine ? YoneSword.Devil : YoneSword.Divine;
        }
    }

    /// <summary>The ready sword picks the skill: divine dash (knock-up) or devil combo (four magic hits).</summary>
    public sealed class YoneSkill : SkillBehaviour {
        static readonly double[] DivineTimes = { 1.66 };
        static readonly double[] DevilTimes = { 0.12, 0.72, 1.08, 2.24 };
        const double DivineAnimLength = 4;
        const double DevilAnimLength = 4.25;

        readonly YoneAttack attack;
        readonly double divineBaseDmg;
        readonly double divineDmgMul;
        readonly double airborneDuration;
        readonly double energyRegen;
        readonly double devilBaseDmg0, devilDmgMul0, devilBaseDmg1, devilDmgMul1;
        YoneSword sword;

        public YoneSkill(UnitDefinition def, YoneAttack attack) {
            this.attack = attack;
            divineBaseDmg = def.Param(5);
            divineDmgMul = def.Param(6);
            airborneDuration = def.Param(7);
            energyRegen = def.Param(8);
            devilBaseDmg0 = def.Param(9);
            devilDmgMul0 = def.Param(10);
            devilBaseDmg1 = def.Param(11);
            devilDmgMul1 = def.Param(12);
        }

        public override void Begin(CombatUnit self, out double duration) {
            sword = attack?.CurrentSword ?? YoneSword.Divine;
            AnimationLength = sword == YoneSword.Divine ? DivineAnimLength : DevilAnimLength;
            HitTimes = sword == YoneSword.Divine ? DivineTimes : DevilTimes;
            Variant = (int)sword;
            base.Begin(self, out duration);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            if (sword == YoneSword.Divine) {
                target.TakeDamage(self.MakeDamage(DamageType.Physical, false, divineBaseDmg, divineDmgMul), self);
                target.Airborne(airborneDuration);
                // As before, this refund is swallowed: energy cannot be gained while a skill is being cast.
                if (!target.Alive) self.RegenEnergy(energyRegen);
                return;
            }
            var heavy = index == 3;
            target.TakeDamage(self.MakeDamage(DamageType.Magical, self.RollCrit(),
                heavy ? devilBaseDmg1 : devilBaseDmg0, heavy ? devilDmgMul1 : devilDmgMul0), self);
        }
    }
}
