using System;
using System.Collections.Generic;

namespace AutoChess.Core.Combat.Heroes {
    /// <summary>Bomb: silences a target holding enough energy, stuns it otherwise.</summary>
    public sealed class AkaliSkill : SkillBehaviour {
        readonly double energyThreshold;
        readonly double silenceDuration;
        readonly double stunDuration;

        public AkaliSkill(UnitDefinition def) {
            AnimationLength = 1;
            HitTimes = new[] { 0.2 };
            energyThreshold = def.Param(0);
            silenceDuration = def.Param(1);
            stunDuration = def.Param(2);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            if (target.Energy >= energyThreshold) target.Silence(silenceDuration);
            else target.Stun(stunDuration);
        }
    }

    /// <summary>Dragon strike: magic damage scaling with physical damage, knocks up.</summary>
    public sealed class IreliaSkill : SkillBehaviour {
        readonly double baseDmg;
        readonly double dmgMul;
        readonly double airborneDuration;

        public IreliaSkill(UnitDefinition def) {
            AnimationLength = 4.8;
            HitTimes = new[] { 4.23 };
            baseDmg = def.Param(0);
            dmgMul = def.Param(1);
            airborneDuration = def.Param(2);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            target.TakeDamage(self.MakeDamage(DamageType.Magical, false, baseDmg, dmgMul), self);
            target.Airborne(airborneDuration);
        }
    }

    /// <summary>Spinning cuts; the first cut on a target applies anti-heal and grants it energy.</summary>
    public sealed class KatarinaSkill : SkillBehaviour {
        readonly double baseDmgPerHit;
        readonly double dmgMulPerHit;
        readonly double antiHealDuration;
        readonly HashSet<int> affected = new();

        public KatarinaSkill(UnitDefinition def) {
            AnimationLength = 3.1;
            var hits = Math.Max(1, (int)def.Param(0));
            var interval = def.Param(1);
            var times = new double[hits];
            for (var i = 0; i < hits; i++) times[i] = i * interval;
            HitTimes = times;
            baseDmgPerHit = def.Param(2);
            dmgMulPerHit = def.Param(3);
            antiHealDuration = def.Param(4);
        }

        public override void OnHit(CombatUnit self, int index) {
            if (index == 0) affected.Clear();
            var target = self.Target;
            if (target == null) return;
            var isNew = affected.Add(target.Id);
            target.TakeDamage(self.MakeDamage(DamageType.Magical, self.RollCrit(), baseDmgPerHit, dmgMulPerHit), self, isNew);
            if (isNew) target.AntiHeal(antiHealDuration);
        }
    }

    /// <summary>Stone skin (defences, max tenacity, slower attacks) then a knock-up slam.</summary>
    public sealed class MalphiteSkill : SkillBehaviour {
        public const string SkinVisual = "malphite_skill";
        readonly double defMul;
        readonly double atkSpeedReduceMul;
        readonly double effectDuration;
        readonly double baseDmg;
        readonly double dmgMul;
        readonly double airborneDuration;
        readonly string effectKey;

        public MalphiteSkill(UnitDefinition def) {
            AnimationLength = 4.7;
            HitTimes = new[] { 0.46, 2.3 };
            defMul = def.Param(0);
            atkSpeedReduceMul = def.Param(1);
            effectDuration = def.Param(2);
            baseDmg = def.Param(3);
            dmgMul = def.Param(4);
            airborneDuration = def.Param(5);
            effectKey = def.SpecialKey(0);
            DrainsEnergy = true;
            DrainDelay = HitTimes[0];
            DrainDuration = effectDuration;
        }

        public override void OnHit(CombatUnit self, int index) {
            if (index == 0) {
                if (!self.Alive) return;
                self.AddModifierSet(ModifierSet.Timed(self, effectKey, effectDuration, new[] {
                    new StatModifier(StatKind.Armor, defMul, ModifierType.Percent),
                    new StatModifier(StatKind.Resistance, defMul, ModifierType.Percent),
                    new StatModifier(StatKind.AttackSpeed, atkSpeedReduceMul, ModifierType.Percent),
                    new StatModifier(StatKind.Tenacity, self.Battle.Rules.MaxTenacity, ModifierType.Flat),
                }, visual: SkinVisual));
                return;
            }
            var target = self.Target;
            if (target == null) return;
            target.TakeDamage(self.MakeDamage(DamageType.Physical, false, baseDmg, dmgMul), self);
            target.Airborne(airborneDuration);
        }
    }

    /// <summary>Opens her wings: heals for magic damage plus a share of max health growing with missing health.</summary>
    public sealed class MorganaSkill : SkillBehaviour {
        readonly double healByHpMulMin;
        readonly double healByHpMulMax;
        readonly double healByDmgMul;

        public MorganaSkill(UnitDefinition def) {
            AnimationLength = 1;
            HitTimes = new[] { 0.41 };
            healByHpMulMin = def.Param(0);
            healByHpMulMax = def.Param(1);
            healByDmgMul = def.Param(2);
        }

        public override void OnHit(CombatUnit self, int index) {
            if (!self.Alive) return;
            var t = Math.Clamp(self.HpLostPercentage, 0, 1);
            self.Heal(self.MagicalDamage * healByDmgMul + self.MaxHp * (healByHpMulMin + (healByHpMulMax - healByHpMulMin) * t));
        }
    }

    /// <summary>Every third attack adds true damage.</summary>
    public sealed class YasuoAttack : AttackBehaviour {
        const double DmgMul = 0.5;
        const int CountLimit = 2;
        int count;

        public YasuoAttack() {
            AnimationLength = 1.333;
            HitTimes = new[] { 0.33 };
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            var damage = self.BasicDamage(DamageType.Physical);
            double dealt;
            if (count == CountLimit) {
                dealt = target.TakeDamage(damage, new Damage(damage.Value * DmgMul, DamageType.True, 0, damage.Crit), self);
                count = 0;
            }
            else {
                dealt = target.TakeDamage(damage, self);
                count++;
            }
            self.StealLife(dealt);
            self.RegenEnergy(self.Def.EnergyRegenPerAttack);
        }
    }

    /// <summary>Knock-up, two cuts, the second one bleeds.</summary>
    public sealed class YasuoSkill : SkillBehaviour {
        readonly double airborneDuration;
        readonly double baseDmg0, dmgMul0, baseDmg1, dmgMul1;
        readonly double dotBaseDmg, dotDmgMul;
        readonly double duration;
        readonly double interval;
        readonly string dotKey;

        public YasuoSkill(UnitDefinition def) {
            AnimationLength = 6.3;
            HitTimes = new[] { 0.7, 1.7, 3.5 };
            airborneDuration = def.Param(0);
            baseDmg0 = def.Param(1);
            dmgMul0 = def.Param(2);
            baseDmg1 = def.Param(3);
            dmgMul1 = def.Param(4);
            dotBaseDmg = def.Param(5);
            dotDmgMul = def.Param(6);
            duration = def.Param(7);
            interval = def.Param(8);
            dotKey = def.SpecialKey(0);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            switch (index) {
                case 0:
                    target.Airborne(airborneDuration);
                    break;
                case 1:
                    target.TakeDamage(self.MakeDamage(DamageType.Physical, false, baseDmg0, dmgMul0), self);
                    break;
                default:
                    target.TakeDamage(self.MakeDamage(DamageType.Physical, false, baseDmg1, dmgMul1), self);
                    var bleed = self.MakeDamage(DamageType.Physical, false, dotBaseDmg, dotDmgMul);
                    target.AddDamageOverTime(DamageOverTime.Create(dotKey, self, bleed,
                        (int)Math.Round(duration / interval), interval, 1, false));
                    break;
            }
        }
    }

    /// <summary>Shuriken on the target, then an explosion that crits and stuns it.</summary>
    public sealed class ZedSkill : SkillBehaviour {
        readonly double dmgMul;
        readonly double stunMain;
        CombatUnit aimed;

        public ZedSkill(UnitDefinition def) {
            AnimationLength = 3;
            HitTimes = new[] { 0.83, 1.66 };
            // Params 0 (base damage), 2 (explosion range) and 4 (stun of the others) are not used yet:
            // the area part of this skill was never implemented.
            dmgMul = def.Param(1);
            stunMain = def.Param(3);
        }

        public override void OnHit(CombatUnit self, int index) {
            if (index == 0) {
                aimed = self.Target;
                aimed?.TakeDamage(self.MakeDamage(DamageType.Physical, false, physicalRatio: dmgMul), self);
                return;
            }
            if (aimed == null) return;
            aimed.TakeDamage(self.MakeDamage(DamageType.Physical, true, physicalRatio: 1d), self);
            aimed.Stun(stunMain);
            aimed = null;
        }

        public override void End(CombatUnit self, bool complete) {
            base.End(self, complete);
            if (!complete) aimed = null;
        }
    }
}
