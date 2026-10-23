using System;

namespace AutoChess.Core.Combat {
    /// <summary>
    /// Auto-attack of one hero (was AttackProcessor). The swing lasts <see cref="AnimationLength"/> seconds of
    /// animation, played faster when the attack speed asks for it; <see cref="HitTimes"/> are animation-time
    /// offsets at which <see cref="OnHit"/> fires. One instance per combat unit, so it may keep state.
    /// </summary>
    public abstract class AttackBehaviour {
        protected double AnimationLength;
        double[] hitTimes = Array.Empty<double>();

        public double[] HitTimes {
            get => hitTimes;
            protected set => hitTimes = value ?? Array.Empty<double>();
        }

        /// <summary>Animation play rate of the current swing (>= 1).</summary>
        public double AnimationSpeed { get; private set; } = 1;

        public virtual void Begin(CombatUnit self, out double duration) {
            var interval = 1 / self.AttackSpeed;
            AnimationSpeed = Math.Max(1, AnimationLength / interval);
            duration = AnimationLength / AnimationSpeed;
        }

        public virtual void OnHit(CombatUnit self, int index) { }

        public virtual void End(CombatUnit self, bool complete) { }
    }

    /// <summary>
    /// Active skill of one hero (was SkillProcessor). Cast when the energy bar is full; <see cref="HitTimes"/>
    /// are seconds since the cast started. Heroes flagged unstoppable ignore crowd control while casting.
    /// </summary>
    public abstract class SkillBehaviour {
        protected double AnimationLength;
        double[] hitTimes = Array.Empty<double>();
        protected bool DrainsEnergy;
        protected double DrainDelay;
        protected double DrainDuration;

        public double[] HitTimes {
            get => hitTimes;
            protected set => hitTimes = value ?? Array.Empty<double>();
        }

        /// <summary>Presentation variant of the current cast (forwarded in UnitSkillStartedEvent).</summary>
        public int Variant { get; protected set; }

        public virtual void Begin(CombatUnit self, out double duration) {
            if (self.Def.Unstoppable) self.Status.Unstoppable = true;
            if (DrainsEnergy) self.DrainEnergy(DrainDelay, DrainDuration);
            else self.UseAllEnergy();
            duration = AnimationLength;
        }

        /// <summary>Called once when the unit enters the battle (passives that hook into it).</summary>
        public virtual void Setup(CombatUnit self) { }

        public virtual void OnHit(CombatUnit self, int index) { }

        public virtual void End(CombatUnit self, bool complete) {
            if (self.Def.Unstoppable) self.Status.Unstoppable = false;
        }
    }

    public sealed class NoAttack : AttackBehaviour { }

    public sealed class NoSkill : SkillBehaviour { }

    /// <summary>Single hit of the attacker's own damage type, with lifesteal and energy on hit.</summary>
    public class BasicAttack : AttackBehaviour {
        readonly DamageType type;

        public BasicAttack(double animationLength, double hitTime, DamageType type) {
            AnimationLength = animationLength;
            HitTimes = new[] { hitTime };
            this.type = type;
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            var dealt = target.TakeDamage(self.BasicDamage(type), self);
            self.StealLife(dealt);
            self.RegenEnergy(self.Def.EnergyRegenPerAttack);
        }
    }
}
