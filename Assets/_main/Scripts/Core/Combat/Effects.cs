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
        public float Duration;
        public bool Permanent => float.IsPositiveInfinity(Duration);
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
        public float Remaining;
        public Mark Mark;
        /// <summary>Presentation hint (VFX name), or null.</summary>
        public string Visual;

        public bool SameAs(ModifierSet other) => Key == other.Key && Owner == other.Owner;

        public static ModifierSet Timed(CombatUnit owner, string key, float duration, StatModifier[] modifiers,
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
                IsPermanent = true, Remaining = float.PositiveInfinity, Visual = visual
            };
            if (mark) set.Mark = new Mark { Owner = owner, Key = key, Stacks = stacks, Duration = float.PositiveInfinity };
            return set;
        }
    }

    public sealed class DamageOverTime {
        public int Id;
        public string Key;
        public CombatUnit Owner;
        public Damage Damage;
        public int Times;
        public float Interval;
        public int Stacks;
        public float Timer;
        public Mark Mark;

        public bool SameAs(DamageOverTime other) => Key == other.Key && Owner == other.Owner;

        /// <param name="applyInstantly">First tick now (true) or after one interval.</param>
        public static DamageOverTime Create(string key, CombatUnit owner, Damage damage, int times, float interval,
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
        public float Amount;
        public int Times;
        public float Interval;
        public float Timer;
        public Mark Mark;

        public bool SameAs(HealOverTime other) => Key == other.Key && Owner == other.Owner;

        public static HealOverTime Create(string key, CombatUnit owner, float amount, int times, float interval,
            bool mark = false) {
            return new HealOverTime {
                Key = key, Owner = owner, Amount = amount, Times = times, Interval = interval, Timer = 0,
                Mark = mark ? new Mark { Owner = owner, Key = key, Stacks = 1, Duration = interval * (times - 1) } : null
            };
        }
    }

    /// <summary>Crowd control timers (was HeroStatusEffects).</summary>
    public sealed class StatusState {
        public float Airborne;
        public float Stun;
        public float Silence;
        public float AntiHeal;
        public bool Unstoppable;

        public bool IsAirborne => Airborne > 0;
        public bool IsStunned => Stun > 0;
        public bool IsSilenced => Silence > 0;
        public bool IsAntiHeal => AntiHeal > 0;

        public void Tick(float dt) {
            if (Airborne > 0) Airborne = Math.Max(0, Airborne - dt);
            if (Stun > 0) Stun = Math.Max(0, Stun - dt);
            if (Silence > 0) Silence = Math.Max(0, Silence - dt);
            if (AntiHeal > 0) AntiHeal = Math.Max(0, AntiHeal - dt);
        }
    }
}
