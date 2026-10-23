using System;
using System.Collections.Generic;

namespace AutoChess.Core.Combat.Heroes {
    /// <summary>
    /// Smoke bomb: silences the enemies around the target; the weakest of them (health share) takes magic damage
    /// growing with its missing health.
    /// </summary>
    public sealed class AkaliSkill : SkillBehaviour {
        public const string SmokeVisual = "akali_smoke";
        public const string StrikeVisual = "akali_strike";
        readonly int radius;
        readonly double silence;
        readonly double baseDmg;
        readonly double dmgMul;
        readonly double missingHpAmp;
        readonly List<CombatUnit> hits = new();

        public AkaliSkill(UnitDefinition def) {
            AnimationLength = 1;
            HitTimes = new[] { 0.2 };
            radius = (int)def.Param(0);
            silence = def.Param(1);
            baseDmg = def.Param(2);
            dmgMul = def.Param(3);
            missingHpAmp = def.Param(4);
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            var battle = self.Battle;
            battle.UnitsInRadius(self.Side.Opponent(), target.Position, radius, hits);
            battle.PlayVisual(SmokeVisual, VisualMode.Burst, target.Position, target.Position, 1.2, radius);
            CombatUnit weakest = null;
            foreach (var u in hits) {
                u.Silence(silence);
                if (weakest == null || u.HpPercentage < weakest.HpPercentage) weakest = u;
            }
            if (weakest == null) return;
            var damage = self.MakeDamage(DamageType.Magical, self.RollCrit(), baseDmg, magicalRatio: dmgMul);
            damage.Value *= 1 + missingHpAmp * weakest.HpLostPercentage;
            battle.PlayVisual(StrikeVisual, VisualMode.Burst, weakest.Position, weakest.Position, 0.6, 1);
            weakest.TakeDamage(damage, self);
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
            self.Battle.PlayVisual("irelia_dragon", VisualMode.Burst, target.Position, target.Position, 1, 1);
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

        int visual = -1;

        public override void Begin(CombatUnit self, out double duration) {
            base.Begin(self, out duration);
            visual = self.Battle.PlayVisual("katarina_spin", VisualMode.Attach, self.Position, self.Position, duration, 1, self);
        }

        public override void End(CombatUnit self, bool complete) {
            base.End(self, complete);
            if (!complete && visual >= 0) self.Battle.StopVisual(visual);
            visual = -1;
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
            self.Battle.PlayVisual("malphite_slam", VisualMode.Burst, target.Position, target.Position, 1, 1);
            target.TakeDamage(self.MakeDamage(DamageType.Physical, false, baseDmg, dmgMul), self);
            target.Airborne(airborneDuration);
        }
    }

    /// <summary>
    /// Black shield on herself and on the ally dealing the most damage (by damage stats): blocks magic damage only and
    /// makes its holder immune to crowd control while it holds.
    /// </summary>
    public sealed class MorganaSkill : SkillBehaviour {
        public const string ShieldVisual = "morgana_shield";
        readonly double baseShield;
        readonly double shieldMul;
        readonly double duration;
        readonly string shieldKey;

        public MorganaSkill(UnitDefinition def) {
            AnimationLength = 1;
            HitTimes = new[] { 0.41 };
            baseShield = def.Param(0);
            shieldMul = def.Param(1);
            duration = def.Param(2);
            shieldKey = def.SpecialKey(0);
        }

        public override void OnHit(CombatUnit self, int index) {
            if (!self.Alive) return;
            CombatUnit carry = null;
            foreach (var u in self.Battle.AliveUnits(self.Side)) {
                if (u == self) continue;
                if (carry == null || Math.Max(u.PhysicalDamage, u.MagicalDamage) * u.AttackSpeed
                    > Math.Max(carry.PhysicalDamage, carry.MagicalDamage) * carry.AttackSpeed) carry = u;
            }
            var amount = baseShield + self.MagicalDamage * shieldMul;
            foreach (var u in new[] { self, carry }) {
                if (u == null) continue;
                u.AddShield(self, shieldKey, amount, duration, DamageType.Magical, blocksCrowdControl: true);
                self.Battle.PlayVisual(ShieldVisual, VisualMode.Attach, u.Position, u.Position, duration, 1, u);
            }
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
                    self.Battle.PlayVisual("yasuo_tornado", VisualMode.Burst, target.Position, target.Position, 1, 1);
                    target.Airborne(airborneDuration);
                    break;
                case 1:
                    self.Battle.PlayVisual("yasuo_slash", VisualMode.Burst, target.Position, target.Position, 0.5, 1);
                    target.TakeDamage(self.MakeDamage(DamageType.Physical, false, baseDmg0, dmgMul0), self);
                    break;
                default:
                    self.Battle.PlayVisual("yasuo_slash", VisualMode.Burst, target.Position, target.Position, 0.5, 1);
                    target.TakeDamage(self.MakeDamage(DamageType.Physical, false, baseDmg1, dmgMul1), self);
                    var bleed = self.MakeDamage(DamageType.Physical, false, dotBaseDmg, dotDmgMul);
                    target.AddDamageOverTime(DamageOverTime.Create(dotKey, self, bleed,
                        (int)Math.Round(duration / interval), interval, 1, false));
                    break;
            }
        }
    }

    /// <summary>
    /// Passive: every time Zed deals or takes enough damage, a shadow clone (a share of his base stats, items
    /// excluded) appears next to the weakest enemy; up to a few at once. Skill: recalls every clone, heals for
    /// their remaining health, then throws a shuriken along a line.
    /// </summary>
    public sealed class ZedSkill : SkillBehaviour {
        public const string ShurikenVisual = "zed_shuriken";
        public const string RecallVisual = "zed_recall";
        public const string CloneVisual = "zed_clone";
        readonly double baseDmg;
        readonly double dmgMul;
        readonly double length;
        readonly double threshold;
        readonly double share;
        readonly int maxClones;
        readonly List<CombatUnit> clones = new();
        readonly List<CombatUnit> hits = new();
        int pending;

        public ZedSkill(UnitDefinition def) {
            AnimationLength = 3;
            HitTimes = new[] { 0.83, 1.66 };
            baseDmg = def.Param(0);
            dmgMul = def.Param(1);
            length = def.Param(2);
            threshold = Math.Max(1, def.Param(3));
            share = def.Param(4);
            maxClones = (int)def.Param(5);
        }

        public override void Setup(CombatUnit self) {
            var battle = self.Battle;
            if (!battle.Db.TryGetUnit(HeroIds.ZedShadow, out _)) return;
            var progress = 0d;
            battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (dealt <= 0 || !self.Alive || (source != self && target != self)) return;
                progress += dealt;
                while (progress >= threshold) {
                    progress -= threshold;
                    pending++;
                }
            }));
            // clones join outside hit resolution (unit lists may be iterated while damage lands)
            battle.AddTicker(_ => {
                while (pending > 0) {
                    pending--;
                    if (self.Alive) SpawnClone(self);
                }
            });
        }

        void SpawnClone(CombatUnit self) {
            clones.RemoveAll(c => !c.Alive);
            if (clones.Count >= maxClones) return;
            var battle = self.Battle;
            CombatUnit weakest = null;
            foreach (var o in battle.AliveUnits(self.Side.Opponent())) {
                if (weakest == null || o.HpPercentage < weakest.HpPercentage) weakest = o;
            }
            var clone = battle.Summon(HeroIds.ZedShadow, self.Side, weakest?.Position ?? self.Position, self.Rank);
            if (clone == null) return;
            var stats = new StatBlock();
            UnitStats.Compute(battle.Db, self.Def, self.Rank, Array.Empty<string>(), stats);
            var mods = new List<StatModifier>();
            void Set(StatKind stat, double value) {
                var delta = value - clone[stat];
                if (Math.Abs(delta) > 1e-9) mods.Add(new StatModifier(stat, delta, ModifierType.Flat));
            }
            foreach (var stat in new[] { StatKind.MaxHp, StatKind.PhysicalDamage, StatKind.MagicalDamage, StatKind.Armor, StatKind.Resistance }) {
                Set(stat, stats[stat] * share);
            }
            foreach (var stat in new[] { StatKind.AttackSpeed, StatKind.CriticalChance, StatKind.CriticalDamage, StatKind.PhysicalPenetration }) {
                Set(stat, stats[stat]);
            }
            clone.AddModifierSet(ModifierSet.Permanent(clone, "zed_shadow", mods.ToArray(), mark: false));
            clones.Add(clone);
            battle.PlayVisual(CloneVisual, VisualMode.Burst, clone.Position, clone.Position, 0.8, 1);
        }

        public override void OnHit(CombatUnit self, int index) {
            var battle = self.Battle;
            if (index == 0) {
                var healed = 0d;
                foreach (var clone in clones) {
                    if (!clone.Alive) continue;
                    healed += clone.Hp;
                    battle.PlayVisual(RecallVisual, VisualMode.Travel, clone.Position, self.Position, 0.35);
                    clone.Despawn();
                }
                clones.Clear();
                if (healed > 0 && self.Alive) self.Heal(healed);
                return;
            }
            var target = self.Target;
            if (target == null) return;
            var end = battle.UnitsOnLine(self.Side.Opponent(), self.Position, target.Position, length, hits);
            battle.PlayVisual(ShurikenVisual, VisualMode.Travel, self.Position, end, 0.35);
            var damage = self.MakeDamage(DamageType.Physical, self.RollCrit(), baseDmg, dmgMul);
            foreach (var u in hits) u.TakeDamage(damage, self);
        }
    }

    /// <summary>Cries: magic damage growing with his armor and resistance, and a short stun around himself.</summary>
    public sealed class AmumuSkill : SkillBehaviour {
        public const string CurseVisual = "amumu_curse";
        readonly double baseDmg;
        readonly double defMul;
        readonly int radius;
        readonly double stun;
        readonly List<CombatUnit> hits = new();

        public AmumuSkill(UnitDefinition def) {
            AnimationLength = 1.667;
            HitTimes = new[] { 0.75 };
            baseDmg = def.Param(0);
            defMul = def.Param(1);
            radius = (int)def.Param(2);
            stun = def.Param(3);
        }

        public override void OnHit(CombatUnit self, int index) {
            if (!self.Alive) return;
            self.Battle.UnitsInRadius(self.Side.Opponent(), self.Position, radius, hits);
            self.Battle.PlayVisual(CurseVisual, VisualMode.Burst, self.Position, self.Position, 1.2, radius);
            var damage = new Damage(baseDmg + (self.Armor + self.Resistance) * defMul, DamageType.Magical, self.MagicalPenetration);
            foreach (var u in hits) {
                u.TakeDamage(damage, self);
                u.Stun(stun);
            }
        }
    }

    /// <summary>
    /// Grabs the furthest enemy and pulls it next to him (knocked up on the way), then hits it (magic damage from
    /// max health) and shreds its armor and resistance. Passive: the first time his health drops below a
    /// threshold he gains a shield.
    /// </summary>
    public sealed class BlitzcrankSkill : SkillBehaviour {
        public const string GrabVisual = "blitzcrank_grab";
        public const string ShieldVisual = "blitzcrank_shield";
        public const string PunchVisual = "blitzcrank_punch";
        readonly double baseDmg;
        readonly double dmgByMaxHp;
        readonly double shred;
        readonly double shredDuration;
        readonly double pullDuration;
        readonly double shieldThreshold;
        readonly double shieldByMaxHp;
        readonly double shieldDuration;
        readonly string shieldKey;
        readonly string shredKey;
        CombatUnit grabbed;

        public BlitzcrankSkill(UnitDefinition def) {
            AnimationLength = 1.8;
            baseDmg = def.Param(0);
            dmgByMaxHp = def.Param(1);
            shred = def.Param(2);
            shredDuration = def.Param(3);
            pullDuration = def.Param(4);
            shieldThreshold = def.Param(5);
            shieldByMaxHp = def.Param(6);
            shieldDuration = def.Param(7);
            shieldKey = def.SpecialKey(0);
            shredKey = def.SpecialKey(1);
            HitTimes = new[] { 0.5, 0.5 + pullDuration };
        }

        public override void Setup(CombatUnit self) {
            var used = false;
            self.Battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (used || target != self || !self.Alive || self.HpPercentage >= shieldThreshold) return;
                used = true;
                self.AddShield(self, shieldKey, self.MaxHp * shieldByMaxHp, shieldDuration);
                self.Battle.PlayVisual(ShieldVisual, VisualMode.Attach, self.Position, self.Position, shieldDuration, 1, self);
            }));
        }

        public override void OnHit(CombatUnit self, int index) {
            if (index == 0) {
                grabbed = self.Battle.FurthestOpponent(self);
                if (grabbed == null) return;
                self.Battle.PlayVisual(GrabVisual, VisualMode.Beam, self.Position, grabbed.Position, pullDuration);
                self.Battle.Displace(grabbed, self.Position, pullDuration);
                return;
            }
            if (grabbed == null || !grabbed.Alive) return;
            self.Battle.PlayVisual(PunchVisual, VisualMode.Burst, grabbed.Position, grabbed.Position, 0.6, 1);
            grabbed.TakeDamage(new Damage(baseDmg + self.MaxHp * dmgByMaxHp, DamageType.Magical, self.MagicalPenetration), self);
            grabbed.AddModifierSet(ModifierSet.Timed(self, shredKey, shredDuration, new[] {
                new StatModifier(StatKind.Armor, -Math.Abs(shred), ModifierType.Percent),
                new StatModifier(StatKind.Resistance, -Math.Abs(shred), ModifierType.Percent),
            }));
            grabbed = null;
        }
    }

    /// <summary>Snips along a line through the target (and one cell behind it), healing from each; the last snip is true damage on the target.</summary>
    public sealed class GwenSkill : SkillBehaviour {
        public const string SnipVisual = "gwen_snip";
        readonly int snips;
        readonly double baseDmg;
        readonly double dmgMul;
        readonly double vamp;
        readonly List<CombatUnit> hits = new();

        public GwenSkill(UnitDefinition def) {
            AnimationLength = 2.667;
            snips = Math.Max(1, (int)def.Param(0));
            baseDmg = def.Param(1);
            dmgMul = def.Param(2);
            vamp = def.Param(3);
            var times = new double[snips];
            for (var i = 0; i < snips; i++) times[i] = 0.45 + i * 1.6 / Math.Max(1, snips - 1);
            HitTimes = times;
        }

        public override void OnHit(CombatUnit self, int index) {
            var target = self.Target;
            if (target == null) return;
            var value = baseDmg + self.MagicalDamage * dmgMul;
            HeroLines.ThroughTarget(self, target, 1, hits);
            self.Battle.PlayVisual(SnipVisual, VisualMode.Burst, target.Position, target.Position, 0.6, 1);
            var dealt = 0d;
            foreach (var u in hits) {
                var last = index == snips - 1 && u == target;
                var damage = last ? new Damage(value, DamageType.True, 0) : new Damage(value, DamageType.Magical, self.MagicalPenetration);
                dealt += u.TakeDamage(damage, self, index == 0);
            }
            if (self.Alive) self.Heal(dealt * vamp);
        }
    }

    /// <summary>Line helpers shared by skills.</summary>
    static class HeroLines {
        /// <summary>
        /// Enemies on the line from <paramref name="self"/> through <paramref name="target"/>, up to
        /// <paramref name="behind"/> cells past it (the target always included).
        /// </summary>
        public static void ThroughTarget(CombatUnit self, CombatUnit target, double behind, List<CombatUnit> into) {
            var battle = self.Battle;
            var cells = battle.Grid.Distance(battle.CellOf(self), battle.CellOf(target));
            battle.UnitsOnLine(self.Side.Opponent(), self.Position, target.Position, cells + behind, into);
            if (!into.Contains(target)) into.Insert(0, target);
        }
    }

    /// <summary>
    /// Passive: jumps next to his target instead of walking (waits when no cell there is free). Skill: kicks the
    /// target far away (physical damage, knock-up); enemies around where it lands take part of the damage and are
    /// knocked up too; kicked onto the board's edge it is also stunned. Then he jumps after it.
    /// </summary>
    public sealed class LeeSinSkill : SkillBehaviour {
        public const string KickVisual = "leesin_kick";
        public const string LandVisual = "leesin_land";
        const double FlightTime = 0.5;
        readonly double baseDmg;
        readonly double dmgMul;
        readonly double distance;
        readonly double airborne;
        readonly double splashMul;
        readonly double edgeStun;
        readonly double leapDuration;
        readonly List<CombatUnit> hits = new();
        CombatUnit kicked;
        int landing = -1;
        double value;

        public LeeSinSkill(UnitDefinition def) {
            AnimationLength = 1.3;
            HitTimes = new[] { 0.35, 0.35 + FlightTime };
            baseDmg = def.Param(0);
            dmgMul = def.Param(1);
            distance = def.Param(2);
            airborne = def.Param(3);
            splashMul = def.Param(4);
            edgeStun = def.Param(5);
            leapDuration = def.Param(6) > 0 ? def.Param(6) : 0.4;
        }

        public override void Setup(CombatUnit self) {
            self.Leaps = true;
            self.LeapDuration = leapDuration;
        }

        public override void OnHit(CombatUnit self, int index) {
            var battle = self.Battle;
            // locals: damage can kill Lee (thorns...) mid-hit, and End then clears the fields
            if (index == 0) {
                var target = self.Target;
                kicked = null;
                if (target == null) return;
                var damage = self.MakeDamage(DamageType.Physical, self.RollCrit(), baseDmg, dmgMul);
                value = damage.Value;
                battle.PlayVisual(KickVisual, VisualMode.Burst, target.Position, target.Position, 0.8, 1);
                target.TakeDamage(damage, self);
                if (!target.Alive || !self.Alive) return;
                var dx = target.Position.X - self.Position.X;
                var dy = target.Position.Y - self.Position.Y;
                var norm = Math.Sqrt(dx * dx + dy * dy);
                var away = norm < 1e-9 ? target.Position
                    : target.Position + new Vec2(dx / norm, dy / norm) * (distance * battle.Grid.AverageCellDistance);
                landing = battle.Displace(target, away, FlightTime);
                if (landing < 0) target.Airborne(airborne);
                kicked = target;
                return;
            }
            var victim = kicked;
            var cell = landing;
            kicked = null;
            landing = -1;
            if (victim == null || cell < 0) return;
            if (victim.Alive) {
                victim.Airborne(airborne);
                if (battle.IsEdgeCell(cell)) victim.Stun(edgeStun);
            }
            battle.UnitsInRadius(self.Side.Opponent(), battle.Grid.Center(cell), 1, hits);
            battle.PlayVisual(LandVisual, VisualMode.Burst, battle.Grid.Center(cell), battle.Grid.Center(cell), 1, 1);
            var splash = new Damage(value * splashMul, DamageType.Physical, self.PhysicalPenetration);
            foreach (var u in hits) {
                if (u == victim) continue;
                u.TakeDamage(splash, self);
                u.Airborne(airborne);
            }
        }

        public override void End(CombatUnit self, bool complete) {
            base.End(self, complete);
            if (!complete) {
                kicked = null;
                landing = -1;
            }
            // chase the kicked enemy right away
            if (complete && self.Target != null) self.Battle.LeapTo(self, self.Target, leapDuration);
        }
    }
}
