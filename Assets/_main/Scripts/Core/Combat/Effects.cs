using System;

namespace AutoChess.Core.Combat {
    /// <summary>
    /// Icon + stack counter shown over a unit (Teemo's ignite, Yone's weakening...). Abilities read the stacks
    /// back to grow their effects; the owning effect decides when the mark goes away.
    /// </summary>
    public sealed class Mark {
        public int Id;
        public CombatUnit Owner;
        public string Key;
        public int Stacks;
        public double Duration;
        public bool Permanent => double.IsPositiveInfinity(Duration);
    }

    /// <summary>Absorbs damage before health until it is used up or expires. One per key and owner.</summary>
    public sealed class Shield {
        public CombatUnit Owner;
        public string Key;
        public double Amount;
        public double Remaining;
        /// <summary>Absorbs only this damage type (a spell shield), or every type when null.</summary>
        public DamageType? Only;
        /// <summary>While it holds, its unit is immune to crowd control.</summary>
        public bool BlocksCrowdControl;
    }

    /// <summary>A forced slide (knock-back / pull) from one point to a cell center.</summary>
    public sealed class DisplacementState {
        Vec2 from;
        Vec2 to;
        double elapsed;
        double duration;

        public bool Active { get; private set; }

        internal void Start(Vec2 from, Vec2 to, double duration) {
            this.from = from;
            this.to = to;
            this.duration = duration;
            elapsed = 0;
            Active = true;
        }

        /// <summary>Position after <paramref name="dt"/> more seconds of the slide.</summary>
        internal Vec2 Tick(double dt) {
            elapsed += dt;
            if (duration <= 0 || elapsed >= duration) {
                Active = false;
                return to;
            }
            return Vec2.Lerp(from, to, elapsed / duration);
        }
    }

    /// <summary>A group of stat modifiers applied together (an item, an evolution, a buff, a debuff).</summary>
    public sealed class ModifierSet {
        public int Id;
        public string Key;
        /// <summary>Who applied it. A new set with the same key and owner replaces the old one.</summary>
        public CombatUnit Owner;
        public StatModifier[] Modifiers;
        public int Stacks = 1;
        public bool IsPermanent;
        public double Remaining;
        public Mark Mark;
        /// <summary>Presentation hint (VFX name), or null.</summary>
        public string Visual;

        public bool SameAs(ModifierSet other) => Key == other.Key && Owner == other.Owner;

        public static ModifierSet Timed(CombatUnit owner, string key, double duration, StatModifier[] modifiers,
            int stacks = 1, bool mark = true, string visual = null) {
            var set = new ModifierSet {
                Key = key, Owner = owner, Modifiers = modifiers, Stacks = stacks,
                IsPermanent = false, Remaining = duration, Visual = visual
            };
            if (mark) set.Mark = new Mark { Owner = owner, Key = key, Stacks = stacks, Duration = duration };
            return set;
        }

        public static ModifierSet Permanent(CombatUnit owner, string key, StatModifier[] modifiers, int stacks = 1,
            bool mark = true, string visual = null) {
            var set = new ModifierSet {
                Key = key, Owner = owner, Modifiers = modifiers, Stacks = stacks,
                IsPermanent = true, Remaining = double.PositiveInfinity, Visual = visual
            };
            if (mark) set.Mark = new Mark { Owner = owner, Key = key, Stacks = stacks, Duration = double.PositiveInfinity };
            return set;
        }
    }

    public sealed class DamageOverTime {
        public int Id;
        public string Key;
        public CombatUnit Owner;
        public Damage Damage;
        public int Times;
        public double Interval;
        public int Stacks;
        public double Timer;
        public Mark Mark;

        public bool SameAs(DamageOverTime other) => Key == other.Key && Owner == other.Owner;

        /// <param name="applyInstantly">First tick now (true) or after one interval.</param>
        public static DamageOverTime Create(string key, CombatUnit owner, Damage damage, int times, double interval,
            int stacks = 1, bool applyInstantly = true, bool mark = true) {
            return new DamageOverTime {
                Key = key, Owner = owner, Damage = damage, Times = times, Interval = interval, Stacks = stacks,
                Timer = applyInstantly ? 0 : interval,
                Mark = mark
                    ? new Mark { Owner = owner, Key = key, Stacks = stacks, Duration = interval * (applyInstantly ? times - 1 : times) }
                    : null
            };
        }
    }

    public sealed class HealOverTime {
        public int Id;
        public string Key;
        public CombatUnit Owner;
        public double Amount;
        public int Times;
        public double Interval;
        public double Timer;
        public Mark Mark;

        public bool SameAs(HealOverTime other) => Key == other.Key && Owner == other.Owner;

        public static HealOverTime Create(string key, CombatUnit owner, double amount, int times, double interval,
            bool mark = false) {
            return new HealOverTime {
                Key = key, Owner = owner, Amount = amount, Times = times, Interval = interval, Timer = 0,
                Mark = mark ? new Mark { Owner = owner, Key = key, Stacks = 1, Duration = interval * (times - 1) } : null
            };
        }
    }

    /// <summary>Crowd control timers (was HeroStatusEffects).</summary>
    public sealed class StatusState {
        public double Airborne;
        public double Stun;
        public double Silence;
        public double AntiHeal;
        /// <summary>Immune to damage and crowd control.</summary>
        public double Invulnerable;
        public bool Unstoppable;
        /// <summary>Seconds left immune to crowd control (an item), damage still lands.</summary>
        public double CrowdControlImmune;

        public bool IsAirborne => Airborne > 0;
        public bool IsCrowdControlImmune => CrowdControlImmune > 0;
        public bool IsStunned => Stun > 0;
        public bool IsSilenced => Silence > 0;
        public bool IsAntiHeal => AntiHeal > 0;
        public bool IsInvulnerable => Invulnerable > 0;

        public void Tick(double dt) {
            if (Airborne > 0) Airborne = Math.Max(0, Airborne - dt);
            if (Stun > 0) Stun = Math.Max(0, Stun - dt);
            if (Silence > 0) Silence = Math.Max(0, Silence - dt);
            if (AntiHeal > 0) AntiHeal = Math.Max(0, AntiHeal - dt);
            if (Invulnerable > 0) Invulnerable = Math.Max(0, Invulnerable - dt);
            if (CrowdControlImmune > 0) CrowdControlImmune = Math.Max(0, CrowdControlImmune - dt);
        }
    }
}
