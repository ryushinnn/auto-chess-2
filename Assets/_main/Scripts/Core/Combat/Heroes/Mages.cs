using System.Collections.Generic;
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

    /// <summary>Three bombs, stronger per ignite stack.</summary>
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
            self.Battle.PlayVisual("teemo_bomb", VisualMode.Travel, self.Position, target.Position, 0.3);
            var main = self.MakeDamage(DamageType.Magical, false,
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
                self.Battle.PlayVisual("yone_divine", VisualMode.Burst, target.Position, target.Position, 0.6, 1);
                target.TakeDamage(self.MakeDamage(DamageType.Physical, false, divineBaseDmg, divineDmgMul), self);
                target.Airborne(airborneDuration);
                // As before, this refund is swallowed: energy cannot be gained while a skill is being cast.
                if (!target.Alive) self.RegenEnergy(energyRegen);
                return;
            }
            var heavy = index == 3;
            self.Battle.PlayVisual("yone_devil", VisualMode.Burst, target.Position, target.Position, 0.5, heavy ? 1.5 : 1);
            target.TakeDamage(self.MakeDamage(DamageType.Magical, self.RollCrit(),
                heavy ? devilBaseDmg1 : devilBaseDmg0, heavy ? devilDmgMul1 : devilDmgMul0), self);
        }
    }

    /// <summary>Orb along a line through the target: magic damage out, the same as true damage back; heals.</summary>
    public sealed class AhriSkill : SkillBehaviour {
        public const string OrbVisual = "ahri_orb";
        readonly double baseDmg;
        readonly double dmgMul;
        readonly double length;
        readonly double healMul;
        readonly List<CombatUnit> hits = new();
        Vec2 orbEnd;
        double value;
        double dealt;

        public AhriSkill(UnitDefinition def) {
            AnimationLength = 2.567;
            HitTimes = new[] { 0.7, 1.5 };
            baseDmg = def.Param(0);
            dmgMul = def.Param(1);
            length = def.Param(2);
            healMul = def.Param(3);
        }

        public override void OnHit(CombatUnit self, int index) {
            if (index == 0) {
                var target = self.Target;
                if (target == null) return;
                dealt = 0;
                value = baseDmg + self.MagicalDamage * dmgMul;
                orbEnd = self.Battle.UnitsOnLine(self.Side.Opponent(), self.Position, target.Position, length, hits);
                self.Battle.PlayVisual(OrbVisual, VisualMode.Travel, self.Position, orbEnd, 0.5);
                foreach (var u in hits) dealt += u.TakeDamage(new Damage(value, DamageType.Magical, self.MagicalPenetration), self);
            }
            else {
                if (value <= 0) return;
                self.Battle.UnitsOnLine(self.Side.Opponent(), orbEnd, self.Position, length, hits);
                self.Battle.PlayVisual(OrbVisual, VisualMode.Travel, orbEnd, self.Position, 0.5);
                foreach (var u in hits) dealt += u.TakeDamage(new Damage(value, DamageType.True, 0), self, false);
                value = 0;
                if (self.Alive) self.Heal(dealt * healMul);
            }
        }
    }

    /// <summary>Tibbers slams the target: full damage and a long stun on it, less damage and a shorter stun around it.</summary>
    public sealed class AnnieSkill : SkillBehaviour {
        public const string SlamVisual = "annie_tibbers";
        readonly double baseDmg;
        readonly double dmgMul;
        readonly double splashMul;
        readonly double stunCenter;
        readonly double stunSplash;
        readonly List<CombatUnit> hits = new();

        public AnnieSkill(UnitDefinition def) {
            AnimationLength = 1.8;
            HitTimes = new[] { 0.9 };
            baseDmg = def.Param(0);
            dmgMul = def.Param(1);
            splashMul = def.Param(2);
            stunCenter = def.Param(3);
            stunSplash = def.Param(4);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            self.Battle.UnitsInRadius(self.Side.Opponent(), target.Position, 1, hits);
            if (!hits.Contains(target)) hits.Insert(0, target);
            self.Battle.PlayVisual(SlamVisual, VisualMode.Burst, target.Position, target.Position, 1.2, 1);
            var damage = self.MakeDamage(DamageType.Magical, false, baseDmg, magicalRatio: dmgMul);
            foreach (var u in hits) {
                var center = u == target;
                u.TakeDamage(center ? damage : new Damage(damage.Value * splashMul, damage.Type, damage.Penetration), self);
                u.Stun(center ? stunCenter : stunSplash);
            }
        }
    }

    /// <summary>Channels a monsoon: heals nearby allies every interval until it ends or hard crowd control stops it.</summary>
    public sealed class JannaSkill : SkillBehaviour {
        public const string MonsoonVisual = "janna_monsoon";
        readonly double baseHeal;
        readonly double healMul;
        readonly int radius;
        readonly List<CombatUnit> allies = new();
        int visual = -1;

        public JannaSkill(UnitDefinition def) {
            var duration = Math.Min(def.Param(0), 4);
            var interval = Math.Max(def.Param(1), 0.1);
            AnimationLength = duration;
            var ticks = (int)Math.Floor(duration / interval + 1e-9);
            var times = new double[ticks];
            for (var i = 0; i < ticks; i++) times[i] = (i + 1) * interval;
            HitTimes = times;
            baseHeal = def.Param(2);
            healMul = def.Param(3);
            radius = (int)def.Param(4);
        }

        public override void Begin(CombatUnit self, out double duration) {
            base.Begin(self, out duration);
            visual = self.Battle.PlayVisual(MonsoonVisual, VisualMode.Attach, self.Position, self.Position, duration, radius, self);
        }

        public override void End(CombatUnit self, bool complete) {
            base.End(self, complete);
            if (!complete && visual >= 0) self.Battle.StopVisual(visual);
            visual = -1;
        }

        public override void OnHit(CombatUnit self, int index) {
            if (!self.Alive) return;
            self.Battle.UnitsInRadius(self.Side, self.Position, radius, allies);
            var heal = baseHeal + self.MagicalDamage * healMul;
            foreach (var u in allies) u.Heal(heal);
        }
    }

    /// <summary>Grows the weakest ally: bonus health (healed in) for a while, knocks up enemies next to it.</summary>
    public sealed class LuluSkill : SkillBehaviour {
        public const string GrowthVisual = "lulu_growth";
        readonly double baseHp;
        readonly double hpMul;
        readonly double duration;
        readonly double airborne;
        readonly string effectKey;
        readonly List<CombatUnit> hits = new();

        public LuluSkill(UnitDefinition def) {
            AnimationLength = 1.3;
            HitTimes = new[] { 0.5 };
            baseHp = def.Param(0);
            hpMul = def.Param(1);
            duration = def.Param(2);
            airborne = def.Param(3);
            effectKey = def.SpecialKey(0);
        }

        public override void OnHit(CombatUnit self, int index) {
            var ally = self.Battle.WeakestAlly(self);
            if (ally == null) return;
            var bonus = baseHp + self.MagicalDamage * hpMul;
            ally.AddModifierSet(ModifierSet.Timed(self, effectKey, duration, new[] {
                new StatModifier(StatKind.MaxHp, bonus, ModifierType.Flat),
            }));
            ally.Heal(bonus);
            self.Battle.PlayVisual(GrowthVisual, VisualMode.Attach, ally.Position, ally.Position, duration, 1, ally);
            self.Battle.UnitsInRadius(self.Side.Opponent(), ally.Position, 1, hits);
            foreach (var u in hits) u.Airborne(airborne);
        }
    }

    /// <summary>
    /// Passive: her damage grows with the distance to the enemy (none next to her). Skill: a beam along a line
    /// through the target, across the board.
    /// </summary>
    public sealed class LuxSkill : SkillBehaviour {
        public const string SparkVisual = "lux_spark";
        readonly double baseDmg;
        readonly double dmgMul;
        readonly double length;
        readonly double bonusPerCell;
        readonly List<CombatUnit> hits = new();

        public LuxSkill(UnitDefinition def) {
            AnimationLength = 2.0;
            HitTimes = new[] { 0.9 };
            baseDmg = def.Param(0);
            dmgMul = def.Param(1);
            length = def.Param(2);
            bonusPerCell = def.Param(3);
        }

        public override void Setup(CombatUnit self) {
            var battle = self.Battle;
            battle.AddHitHook(new HitHook(before: (CombatUnit target, CombatUnit source, ref Damage damage, HitKind kind) => {
                if (source != self || target == null) return true;
                var cells = battle.Grid.Distance(battle.CellOf(self), battle.CellOf(target));
                if (cells > 1) damage.Value *= 1 + bonusPerCell * (cells - 1);
                return true;
            }));
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            var end = self.Battle.UnitsOnLine(self.Side.Opponent(), self.Position, target.Position, length, hits);
            self.Battle.PlayVisual(SparkVisual, VisualMode.Beam, self.Position, end, 0.8);
            var damage = self.MakeDamage(DamageType.Magical, false, baseDmg, magicalRatio: dmgMul);
            foreach (var u in hits) u.TakeDamage(damage, self);
        }
    }
}
