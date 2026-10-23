using System;
using System.Collections.Generic;

namespace AutoChess.Core.Combat {
    /// <summary>
    /// A unit inside one battle: stats, health, energy, crowd control, modifiers, marks, DoT/HoT and the
    /// attack / skill / movement state machines (was BattleHero + HeroAttributes, HeroAttack, HeroSkill,
    /// HeroMovement, HeroStatusEffects, HeroMark). Pure data + rules; the view reads it, never writes it.
    /// </summary>
    public sealed class CombatUnit {
        public readonly int Id;
        public readonly TeamSide Side;
        public readonly UnitDefinition Def;
        public readonly HeroRank Rank;
        public readonly int SourceUnitId;
        public readonly IReadOnlyList<string> Items;
        public readonly BattleSimulation Battle;
        public readonly double HpScale;
        public readonly double DamageScale;

        readonly GameRules rules;
        readonly StatBlock stats = new();

        public double Hp { get; private set; }
        public double Energy { get; private set; }
        public bool Alive { get; private set; } = true;
        public CombatUnit Target { get; internal set; }

        public Vec2 Position;
        /// <summary>Position at the start of the current tick (for view interpolation).</summary>
        public Vec2 PreviousPosition;
        /// <summary>Grid cell this unit holds, -1 while walking between cells.</summary>
        public int OccupiedCell = -1;

        public readonly StatusState Status = new();
        public readonly AttackState Attack;
        public readonly SkillState Skill;
        public readonly MovementState Movement = new();
        public readonly DisplacementState Displacement = new();

        readonly List<ModifierSet> modifierSets = new();
        readonly List<Mark> marks = new();
        readonly List<DamageOverTime> damageOverTimes = new();
        readonly List<HealOverTime> healOverTimes = new();
        readonly List<Shield> shields = new();
        double drainRemaining;

        /// <summary>Total of the active shields.</summary>
        public double ShieldAmount { get; private set; }

        public IReadOnlyList<ModifierSet> ModifierSets => modifierSets;
        public IReadOnlyList<Mark> Marks => marks;
        public IReadOnlyList<DamageOverTime> DamageOverTimes => damageOverTimes;
        public IReadOnlyList<HealOverTime> HealOverTimes => healOverTimes;

        internal CombatUnit(BattleSimulation battle, int id, TeamSide side, UnitDefinition def, CombatantSetup setup,
            AttackBehaviour attack, SkillBehaviour skill) {
            Battle = battle;
            rules = battle.Rules;
            Id = id;
            Side = side;
            Def = def;
            Rank = setup.Rank;
            SourceUnitId = setup.SourceUnitId;
            Items = setup.Items ?? Array.Empty<string>();
            HpScale = setup.HpScale;
            DamageScale = setup.DamageScale;
            Attack = new AttackState(attack);
            Skill = new SkillState(skill);

            for (var s = 0; s < StatBlock.Count; s++) stats[(StatKind)s] = UnitStats.BaseValue(rules, def, (StatKind)s, HpScale, DamageScale);
            Hp = stats[StatKind.MaxHp];
            Energy = Math.Clamp(def.StartingEnergy, 0, rules.MaxEnergy);
        }

        /// <summary>Evolution and item bonuses; called once when the battle is built.</summary>
        internal void ApplyLoadout() {
            var evolution = Def.EvolutionFor(Rank);
            if (evolution.Length > 0) {
                AddModifierSet(ModifierSet.Permanent(this, $"[EVOLUTION_{Rank}]", evolution, mark: false));
            }
            for (var i = 0; i < Items.Count; i++) {
                var item = Battle.Db.Item(Items[i]);
                AddModifierSet(ModifierSet.Permanent(this, $"[ITEM_#{i}]", item.Modifiers, mark: false));
            }
            Hp = MaxHp;
            Skill.Behaviour.Setup(this);
        }

        // ------------------------------------------------------------------ stats

        public double this[StatKind stat] => stats[stat];
        public double MaxHp => stats[StatKind.MaxHp];
        public double PhysicalDamage => stats[StatKind.PhysicalDamage];
        public double MagicalDamage => stats[StatKind.MagicalDamage];
        public double Armor => stats[StatKind.Armor];
        public double Resistance => stats[StatKind.Resistance];
        public double AttackSpeed => stats[StatKind.AttackSpeed];
        public double CriticalChance => stats[StatKind.CriticalChance];
        public double CriticalDamage => stats[StatKind.CriticalDamage];
        public double EnergyRegenEfficiency => stats[StatKind.EnergyRegenEfficiency];
        public double PhysicalPenetration => stats[StatKind.PhysicalPenetration];
        public double MagicalPenetration => stats[StatKind.MagicalPenetration];
        public double LifeSteal => stats[StatKind.LifeSteal];
        public double Tenacity => stats[StatKind.Tenacity];
        public double MovementSpeed => Def.MovementSpeed;
        public int AttackRange => Def.AttackRange;
        public double HpPercentage => MaxHp <= 0 ? 0 : Math.Clamp(Hp / MaxHp, 0, 1);
        public double HpLostPercentage => 1 - HpPercentage;
        public bool IsDrainingEnergy => drainRemaining > 0;

        void Recalculate(StatKind stat) {
            var buffer = Battle.ModifierScratch;
            buffer.Clear();
            foreach (var set in modifierSets) {
                foreach (var m in set.Modifiers) {
                    if (m.Stat == stat) buffer.Add(m);
                }
            }
            var previous = stats[stat];
            stats[stat] = UnitStats.Apply(rules, stat, UnitStats.BaseValue(rules, Def, stat, HpScale, DamageScale), buffer);

            switch (stat) {
                case StatKind.MaxHp:
                    var max = stats[StatKind.MaxHp];
                    if (max > previous) Hp += max - previous;
                    else Hp = Math.Min(Hp, max);
                    break;
                case StatKind.AttackSpeed:
                    Attack.Cooldown = Math.Min(Attack.Cooldown, 1 / AttackSpeed);
                    break;
            }
        }

        void RecalculateAll(StatModifier[] modifiers) {
            // Recalculate each affected stat once.
            var done = 0;
            foreach (var m in modifiers) {
                var bit = 1 << (int)m.Stat;
                if ((done & bit) != 0) continue;
                done |= bit;
                Recalculate(m.Stat);
            }
        }

        // ------------------------------------------------------------------ modifier sets & marks

        public void AddModifierSet(ModifierSet set) {
            if (!Alive) return;
            for (var i = modifierSets.Count - 1; i >= 0; i--) {
                if (!modifierSets[i].SameAs(set)) continue;
                var old = modifierSets[i];
                modifierSets.RemoveAt(i);
                RecalculateAll(old.Modifiers);
                if (old.Mark != null) RemoveMark(old.Mark);
                if (old.Visual != null) Battle.Emit(new EffectRemovedEvent { UnitId = Id, EffectId = old.Id, Visual = old.Visual });
            }

            set.Id = Battle.NextEffectId();
            modifierSets.Add(set);
            RecalculateAll(set.Modifiers);
            if (set.Mark != null) AddMark(set.Mark);
            if (set.Visual != null) {
                Battle.Emit(new EffectAddedEvent {
                    UnitId = Id, EffectId = set.Id, Key = set.Key, Visual = set.Visual, OwnerId = set.Owner?.Id ?? -1,
                    Duration = set.Remaining
                });
            }
        }

        /// <summary>Removes the set with this key and owner (no-op when absent).</summary>
        public void RemoveModifierSet(string key, CombatUnit owner) {
            for (var i = modifierSets.Count - 1; i >= 0; i--) {
                var set = modifierSets[i];
                if (set.Key != key || set.Owner != owner) continue;
                modifierSets.RemoveAt(i);
                RecalculateAll(set.Modifiers);
                if (set.Mark != null) RemoveMark(set.Mark);
                if (set.Visual != null) Battle.Emit(new EffectRemovedEvent { UnitId = Id, EffectId = set.Id, Visual = set.Visual });
            }
        }

        public bool HasModifierSet(string key, CombatUnit owner) {
            foreach (var s in modifierSets) {
                if (s.Key == key && s.Owner == owner) return true;
            }
            return false;
        }

        public void AddMark(Mark mark) {
            if (!Alive) return;
            for (var i = marks.Count - 1; i >= 0; i--) {
                if (marks[i].Key == mark.Key && marks[i].Owner == mark.Owner) {
                    Battle.Emit(new MarkRemovedEvent { UnitId = Id, MarkId = marks[i].Id });
                    marks.RemoveAt(i);
                }
            }
            mark.Id = Battle.NextEffectId();
            marks.Add(mark);
            Battle.Emit(new MarkAddedEvent { UnitId = Id, MarkId = mark.Id, Key = mark.Key, Stacks = mark.Stacks, Duration = mark.Duration });
        }

        public void RemoveMark(Mark mark) {
            if (marks.Remove(mark)) Battle.Emit(new MarkRemovedEvent { UnitId = Id, MarkId = mark.Id });
        }

        public Mark GetMark(string key, CombatUnit owner) {
            foreach (var m in marks) {
                if (m.Key == key && m.Owner == owner) return m;
            }
            return null;
        }

        public int MarkStacks(string key, CombatUnit owner) => GetMark(key, owner)?.Stacks ?? 0;

        public void AddDamageOverTime(DamageOverTime dot) {
            if (!Alive) return;
            for (var i = damageOverTimes.Count - 1; i >= 0; i--) {
                if (!damageOverTimes[i].SameAs(dot)) continue;
                if (damageOverTimes[i].Mark != null) RemoveMark(damageOverTimes[i].Mark);
                damageOverTimes.RemoveAt(i);
            }
            dot.Id = Battle.NextEffectId();
            damageOverTimes.Add(dot);
            if (dot.Mark != null) AddMark(dot.Mark);
        }

        public void AddHealOverTime(HealOverTime hot) {
            if (!Alive) return;
            for (var i = healOverTimes.Count - 1; i >= 0; i--) {
                if (!healOverTimes[i].SameAs(hot)) continue;
                if (healOverTimes[i].Mark != null) RemoveMark(healOverTimes[i].Mark);
                healOverTimes.RemoveAt(i);
            }
            hot.Id = Battle.NextEffectId();
            healOverTimes.Add(hot);
            if (hot.Mark != null) AddMark(hot.Mark);
        }

        // ------------------------------------------------------------------ damage, healing, energy

        public bool RollCrit() => Battle.Rng.NextFloat() < CriticalChance;

        /// <summary>
        /// Damage dealt by this unit: <paramref name="flat"/> + ratios of its physical / magical damage,
        /// multiplied by the crit damage when <paramref name="crit"/>; penetration follows the output type.
        /// </summary>
        public Damage MakeDamage(DamageType type, bool crit, double flat = 0, double physicalRatio = 0, double magicalRatio = 0) {
            var value = flat + PhysicalDamage * physicalRatio + MagicalDamage * magicalRatio;
            if (crit) value *= CriticalDamage;
            return new Damage(value, type, PenetrationFor(type), crit);
        }

        /// <summary>A plain auto-attack of the given type with a crit roll.</summary>
        public Damage BasicDamage(DamageType type) {
            if (type == DamageType.True) return new Damage(0, DamageType.True, 0);
            var value = type == DamageType.Physical ? PhysicalDamage : MagicalDamage;
            var crit = RollCrit();
            if (crit) value *= CriticalDamage;
            return new Damage(value, type, PenetrationFor(type), crit);
        }

        double PenetrationFor(DamageType type) => type switch {
            DamageType.Physical => PhysicalPenetration,
            DamageType.Magical => MagicalPenetration,
            _ => 0
        };

        double Mitigate(Damage damage) {
            double value;
            switch (damage.Type) {
                case DamageType.Physical:
                    value = damage.Value / (1 + Math.Max(rules.MinArmorAndResistance, Armor - damage.Penetration) / rules.DamageReductionConstant);
                    break;
                case DamageType.Magical:
                    value = damage.Value / (1 + Math.Max(rules.MinArmorAndResistance, Resistance - damage.Penetration) / rules.DamageReductionConstant);
                    break;
                default:
                    value = damage.Value;
                    break;
            }
            return Math.Max(value, rules.MinDamageDealt);
        }

        double Apply(Damage damage, CombatUnit source, int stackIndex) {
            if (!Alive) return 0;
            if (Status.IsInvulnerable) {
                Battle.Emit(new UnitDodgedEvent { UnitId = Id, SourceId = source?.Id ?? -1, Invulnerable = true });
                return 0;
            }
            var kind = Battle.CurrentHit;
            if (!Battle.BeforeHit(this, source, ref damage, kind)) {
                Battle.Emit(new UnitDodgedEvent { UnitId = Id, SourceId = source?.Id ?? -1 });
                return 0;
            }
            var dealt = Mitigate(damage);
            var absorbed = Absorb(dealt);
            var toHealth = dealt - absorbed;
            var lost = absorbed + Math.Min(toHealth, Math.Max(0, Hp));
            Hp -= toHealth;
            Battle.Emit(new UnitDamagedEvent {
                UnitId = Id, SourceId = source?.Id ?? -1, Amount = dealt, HpLost = lost, Absorbed = absorbed, Type = damage.Type,
                Crit = damage.Crit, StackIndex = stackIndex
            });
            if (Hp <= 0) Die(source);
            Battle.AfterHit(this, source, damage, dealt, kind);
            return dealt;
        }

        /// <summary>Takes one hit. Returns the post-mitigation damage.</summary>
        public double TakeDamage(Damage damage, CombatUnit source, bool regenEnergy = true) {
            var dealt = Apply(damage, source, 0);
            if (Alive && regenEnergy) RegenEnergy(Def.EnergyRegenPerHit);
            return dealt;
        }

        /// <summary>Takes a two-part hit (shown stacked); energy is regenerated once.</summary>
        public double TakeDamage(Damage first, Damage second, CombatUnit source, bool regenEnergy = true) {
            var dealt = Apply(first, source, 0) + Apply(second, source, 1);
            if (Alive && regenEnergy) RegenEnergy(Def.EnergyRegenPerHit);
            return dealt;
        }

        /// <summary>Adds (or refreshes) a shield of <paramref name="amount"/> for <paramref name="duration"/> seconds.</summary>
        public void AddShield(CombatUnit owner, string key, double amount, double duration) {
            if (!Alive || amount <= 0 || duration <= 0) return;
            for (var i = shields.Count - 1; i >= 0; i--) {
                if (shields[i].Key == key && shields[i].Owner == owner) shields.RemoveAt(i);
            }
            shields.Add(new Shield { Owner = owner, Key = key, Amount = amount, Remaining = duration });
            UpdateShieldTotal();
        }

        /// <summary>Takes <paramref name="damage"/> from the shields, oldest first; returns the absorbed part.</summary>
        double Absorb(double damage) {
            if (shields.Count == 0 || damage <= 0) return 0;
            var absorbed = 0d;
            while (shields.Count > 0 && absorbed < damage) {
                var shield = shields[0];
                var take = Math.Min(shield.Amount, damage - absorbed);
                shield.Amount -= take;
                absorbed += take;
                if (shield.Amount <= 1e-9) shields.RemoveAt(0);
            }
            UpdateShieldTotal();
            return absorbed;
        }

        void UpdateShieldTotal() {
            var total = 0d;
            foreach (var s in shields) total += s.Amount;
            if (Math.Abs(total - ShieldAmount) < 1e-9) return;
            ShieldAmount = total;
            Battle.Emit(new UnitShieldChangedEvent { UnitId = Id, Amount = total });
        }

        public void Heal(double amount) {
            if (!Alive || amount <= 0) return;
            if (amount < 1) amount = 1;
            if (Status.IsAntiHeal) amount *= 1 - rules.AntiHealEfficiency;
            Hp = Math.Min(Hp + amount, MaxHp);
            Battle.Emit(new UnitHealedEvent { UnitId = Id, Amount = amount });
        }

        /// <summary>Lifesteal on damage this unit just dealt.</summary>
        public void StealLife(double dealt) {
            var heal = dealt * LifeSteal;
            if (heal > 0) Heal(heal);
        }

        public void RegenEnergy(double amount) {
            if (!Alive || IsDrainingEnergy || Skill.Active) return;
            Energy = Math.Min(Energy + amount * EnergyRegenEfficiency, rules.MaxEnergy);
        }

        public void UseAllEnergy() => Energy = 0;

        /// <summary>Fills the energy bar (the next decision casts the skill).</summary>
        public void FillEnergy() {
            if (Alive) Energy = rules.MaxEnergy;
        }

        /// <summary>Immune to damage and crowd control for <paramref name="duration"/> seconds.</summary>
        public void MakeInvulnerable(double duration) {
            if (!Alive) return;
            Status.Invulnerable = Math.Max(Status.Invulnerable, duration);
        }

        /// <summary>Empties the energy bar and blocks regeneration for delay + duration seconds.</summary>
        public void DrainEnergy(double delay, double duration) {
            Energy = 0;
            drainRemaining = delay + duration;
            Battle.Emit(new UnitEnergyDrainEvent { UnitId = Id, Delay = delay, Duration = duration });
        }

        // ------------------------------------------------------------------ crowd control

        public void Airborne(double duration) {
            if (!Alive || Status.Unstoppable || Status.IsInvulnerable) return;
            Status.Airborne = duration;
            Battle.Emit(new UnitAirborneEvent { UnitId = Id, Duration = duration });
            InterruptActions();
        }

        public void Stun(double duration) {
            if (!Alive || Status.Unstoppable || Status.IsInvulnerable) return;
            Status.Stun = Math.Max(Status.Stun, duration * (1 - Tenacity));
            InterruptActions();
        }

        public void Silence(double duration) {
            if (!Alive || Status.Unstoppable || Status.IsInvulnerable) return;
            Status.Silence = Math.Max(Status.Silence, duration * (1 - Tenacity));
            Skill.Interrupt(this);
        }

        public void AntiHeal(double duration) {
            if (!Alive) return;
            Status.AntiHeal = Math.Max(Status.AntiHeal, duration);
        }

        void InterruptActions() {
            Attack.Interrupt(this);
            Skill.Interrupt(this);
            Battle.StopMove(this, true);
        }

        // ------------------------------------------------------------------ lifecycle

        void Die(CombatUnit killer) {
            if (!Alive) return;
            Alive = false;
            Hp = 0;
            drainRemaining = 0;
            Attack.Cancel(this);
            Skill.Cancel(this);
            Battle.StopMove(this, true);
            foreach (var set in modifierSets) {
                if (set.Visual != null) Battle.Emit(new EffectRemovedEvent { UnitId = Id, EffectId = set.Id, Visual = set.Visual });
            }
            Battle.OnUnitDied(this, killer);
        }

        /// <summary>Per-tick timers: crowd control, timed modifiers, DoT / HoT, energy drain.</summary>
        internal void TickEffects(double dt) {
            Status.Tick(dt);
            if (drainRemaining > 0) drainRemaining = Math.Max(0, drainRemaining - dt);

            for (var i = modifierSets.Count - 1; i >= 0 && i < modifierSets.Count; i--) {
                var set = modifierSets[i];
                if (set.IsPermanent) continue;
                set.Remaining -= dt;
                if (set.Remaining > 0) continue;
                modifierSets.RemoveAt(i);
                RecalculateAll(set.Modifiers);
                if (set.Mark != null) RemoveMark(set.Mark);
                if (set.Visual != null) Battle.Emit(new EffectRemovedEvent { UnitId = Id, EffectId = set.Id, Visual = set.Visual });
            }

            if (shields.Count > 0) {
                var expired = false;
                for (var i = shields.Count - 1; i >= 0; i--) {
                    shields[i].Remaining -= dt;
                    if (shields[i].Remaining > 0) continue;
                    shields.RemoveAt(i);
                    expired = true;
                }
                if (expired) UpdateShieldTotal();
            }

            for (var i = damageOverTimes.Count - 1; i >= 0 && Alive; i--) {
                if (i >= damageOverTimes.Count) continue;
                var dot = damageOverTimes[i];
                if (dot.Timer <= 0) {
                    using (Battle.Hit(HitKind.Effect)) TakeDamage(dot.Damage, dot.Owner, false);
                    if (--dot.Times <= 0) {
                        damageOverTimes.Remove(dot);
                        if (dot.Mark != null) RemoveMark(dot.Mark);
                    }
                    else {
                        dot.Timer = dot.Interval;
                    }
                }
                else {
                    dot.Timer -= dt;
                }
            }

            for (var i = healOverTimes.Count - 1; i >= 0 && Alive; i--) {
                if (i >= healOverTimes.Count) continue;
                var hot = healOverTimes[i];
                if (hot.Timer <= 0) {
                    Heal(hot.Amount);
                    if (--hot.Times <= 0) {
                        healOverTimes.Remove(hot);
                        if (hot.Mark != null) RemoveMark(hot.Mark);
                    }
                    else {
                        hot.Timer = hot.Interval;
                    }
                }
                else {
                    hot.Timer -= dt;
                }
            }
        }

        public override string ToString() => $"#{Id} {Def.Id}({Rank}) {Side} hp={Hp:0}/{MaxHp:0}";
    }

    // ---------------------------------------------------------------------- action state machines

    public sealed class AttackState {
        public readonly AttackBehaviour Behaviour;
        public double Cooldown;
        public bool Active;
        public double Timer;
        public double Duration;
        public int HitsDone;

        internal AttackState(AttackBehaviour behaviour) {
            Behaviour = behaviour;
        }

        internal bool TryStart(CombatUnit self) {
            if (Cooldown > 0 || self.Skill.Active || self.Status.IsAirborne || self.Status.IsStunned) return false;
            Active = true;
            Timer = 0;
            HitsDone = 0;
            Cooldown = 1 / self.AttackSpeed;
            Behaviour.Begin(self, out Duration);
            self.Battle.Emit(new UnitAttackStartedEvent {
                UnitId = self.Id, TargetId = self.Target?.Id ?? -1, AnimationSpeed = Behaviour.AnimationSpeed, Duration = Duration
            });
            return true;
        }

        internal void Tick(CombatUnit self, double dt) {
            if (Cooldown > 0) Cooldown -= dt;
            if (!Active) return;
            Timer += dt;
            var animTime = Timer * Behaviour.AnimationSpeed;
            var hits = Behaviour.HitTimes;
            while (Active && HitsDone < hits.Length && animTime >= hits[HitsDone]) {
                using (self.Battle.Hit(HitKind.Attack)) Behaviour.OnHit(self, HitsDone++);
            }
            if (Active && Timer > Duration) {
                Active = false;
                Behaviour.End(self, true);
            }
        }

        /// <summary>Crowd control: cancels the swing and restarts the attack cooldown.</summary>
        internal void Interrupt(CombatUnit self) {
            var was = Active;
            Active = false;
            Cooldown = 1 / self.AttackSpeed;
            if (was) {
                Behaviour.End(self, false);
                self.Battle.Emit(new UnitAttackInterruptedEvent { UnitId = self.Id });
            }
        }

        internal void Cancel(CombatUnit self) {
            if (!Active) return;
            Active = false;
            Behaviour.End(self, false);
            self.Battle.Emit(new UnitAttackInterruptedEvent { UnitId = self.Id });
        }
    }

    public sealed class SkillState {
        public readonly SkillBehaviour Behaviour;
        public bool Active;
        public double Timer;
        public double Duration;
        public int HitsDone;

        internal SkillState(SkillBehaviour behaviour) {
            Behaviour = behaviour;
        }

        internal bool TryStart(CombatUnit self) {
            var status = self.Status;
            if (self.Energy < self.Battle.Rules.MaxEnergy || Active || status.IsAirborne || status.IsStunned || status.IsSilenced) {
                return false;
            }
            Active = true;
            Timer = 0;
            HitsDone = 0;
            Behaviour.Begin(self, out Duration);
            self.Battle.Emit(new UnitSkillStartedEvent {
                UnitId = self.Id, TargetId = self.Target?.Id ?? -1, Variant = Behaviour.Variant, Duration = Duration
            });
            return true;
        }

        internal void Tick(CombatUnit self, double dt) {
            if (!Active) return;
            Timer += dt;
            var hits = Behaviour.HitTimes;
            while (Active && HitsDone < hits.Length && Timer >= hits[HitsDone]) {
                Behaviour.OnHit(self, HitsDone++);
            }
            if (Active && Timer > Duration) {
                Active = false;
                Behaviour.End(self, true);
                self.Battle.OnSkillCompleted(self);
            }
        }

        internal void Interrupt(CombatUnit self) {
            if (!Active) return;
            Active = false;
            Behaviour.End(self, false);
            self.Battle.Emit(new UnitSkillInterruptedEvent { UnitId = self.Id });
        }

        internal void Cancel(CombatUnit self) => Interrupt(self);
    }

    public sealed class MovementState {
        /// <summary>Cell of the target when the route was planned (re-plan when it changes).</summary>
        public int TargetCell = -1;
        /// <summary>Reserved destination cell, -1 when idle.</summary>
        public int Destination = -1;
        /// <summary>Cell currently walked to, -1 when idle.</summary>
        public int NextCell = -1;
        public bool Reached;
        public bool Moving;
        public readonly List<int> Waypoints = new();
        public int WaypointIndex;
    }
}
