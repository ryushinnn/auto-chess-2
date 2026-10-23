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

    /// <summary>
    /// Shuriken on the target, then an explosion that crits: the target is stunned, enemies around it (within
    /// the range param, in cells) take the explosion too and are stunned more briefly.
    /// </summary>
    public sealed class ZedSkill : SkillBehaviour {
        readonly double baseDmg;
        readonly double dmgMul;
        readonly int range;
        readonly double stunMain;
        readonly double stunOthers;
        CombatUnit aimed;

        public ZedSkill(UnitDefinition def) {
            AnimationLength = 3;
            HitTimes = new[] { 0.83, 1.66 };
            baseDmg = def.Param(0);
            dmgMul = def.Param(1);
            range = (int)def.Param(2);
            stunMain = def.Param(3);
            stunOthers = def.Param(4);
        }

        public override void OnHit(CombatUnit self, int index) {
            if (index == 0) {
                aimed = self.Target;
                aimed?.TakeDamage(self.MakeDamage(DamageType.Physical, false, baseDmg, dmgMul), self);
                return;
            }
            // Local copy: the hit can kill Zed (a synergy's counter-attack), which cancels the skill and clears the field.
            var target = aimed;
            aimed = null;
            if (target == null || !target.Alive) return;
            var battle = self.Battle;
            var center = target.OccupiedCell >= 0 ? target.OccupiedCell : battle.Grid.NearestIndex(target.Position);
            var victims = new List<CombatUnit>();
            foreach (var e in battle.AliveUnits(self.Side.Opponent())) {
                if (e == target) continue;
                var cell = e.OccupiedCell >= 0 ? e.OccupiedCell : battle.Grid.NearestIndex(e.Position);
                if (battle.Grid.Distance(center, cell) <= range) victims.Add(e);
            }
            target.TakeDamage(self.MakeDamage(DamageType.Physical, true, physicalRatio: 1d), self);
            target.Stun(stunMain);
            foreach (var e in victims) {
                if (!e.Alive || !self.Alive) continue;
                e.TakeDamage(self.MakeDamage(DamageType.Physical, true, physicalRatio: 1d), self);
                e.Stun(stunOthers);
            }
        }

        public override void End(CombatUnit self, bool complete) {
            base.End(self, complete);
            if (!complete) aimed = null;
        }
    }

    /// <summary>Cries: magic damage (grows with max health) and a short stun around himself.</summary>
    public sealed class AmumuSkill : SkillBehaviour {
        public const string CurseVisual = "amumu_curse";
        readonly double baseDmg;
        readonly double dmgByMaxHp;
        readonly int radius;
        readonly double stun;
        readonly List<CombatUnit> hits = new();

        public AmumuSkill(UnitDefinition def) {
            AnimationLength = 1.667;
            HitTimes = new[] { 0.75 };
            baseDmg = def.Param(0);
            dmgByMaxHp = def.Param(1);
            radius = (int)def.Param(2);
            stun = def.Param(3);
        }

        public override void OnHit(CombatUnit self, int index) {
            if (!self.Alive) return;
            self.Battle.UnitsInRadius(self.Side.Opponent(), self.Position, radius, hits);
            self.Battle.PlayVisual(CurseVisual, VisualMode.Burst, self.Position, self.Position, 1.2, radius);
            var damage = new Damage(baseDmg + self.MaxHp * dmgByMaxHp, DamageType.Magical, self.MagicalPenetration);
            foreach (var u in hits) {
                u.TakeDamage(damage, self);
                u.Stun(stun);
            }
        }
    }

    /// <summary>
    /// Grabs the furthest enemy and pulls it next to him (magic damage from max health, stun). Passive: the
    /// first time his health drops below a threshold he gains a shield.
    /// </summary>
    public sealed class BlitzcrankSkill : SkillBehaviour {
        public const string GrabVisual = "blitzcrank_grab";
        public const string ShieldVisual = "blitzcrank_shield";
        readonly double baseDmg;
        readonly double dmgByMaxHp;
        readonly double stun;
        readonly double pullDuration;
        readonly double shieldThreshold;
        readonly double shieldByMaxHp;
        readonly double shieldDuration;
        readonly string shieldKey;
        CombatUnit grabbed;

        public BlitzcrankSkill(UnitDefinition def) {
            AnimationLength = 1.8;
            baseDmg = def.Param(0);
            dmgByMaxHp = def.Param(1);
            stun = def.Param(2);
            pullDuration = def.Param(3);
            shieldThreshold = def.Param(4);
            shieldByMaxHp = def.Param(5);
            shieldDuration = def.Param(6);
            shieldKey = def.SpecialKey(0);
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
            grabbed.TakeDamage(new Damage(baseDmg + self.MaxHp * dmgByMaxHp, DamageType.Magical, self.MagicalPenetration), self);
            grabbed.Stun(stun);
            grabbed = null;
        }
    }

    /// <summary>Snips the target and the enemies next to it several times, healing from each; the last snip is true damage on the target.</summary>
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
            self.Battle.UnitsInRadius(self.Side.Opponent(), target.Position, 1, hits);
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

    /// <summary>
    /// Kicks the target far away (physical damage, knock-up); enemies around where it lands take part of the damage
    /// and are knocked up too. A target kicked onto the edge of the board is stunned as well.
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
        readonly List<CombatUnit> hits = new();
        CombatUnit kicked;
        int landing = -1;
        double value;

        public LeeSinSkill(UnitDefinition def) {
            AnimationLength = 2.567;
            HitTimes = new[] { 0.6, 0.6 + FlightTime };
            baseDmg = def.Param(0);
            dmgMul = def.Param(1);
            distance = def.Param(2);
            airborne = def.Param(3);
            splashMul = def.Param(4);
            edgeStun = def.Param(5);
        }

        public override void OnHit(CombatUnit self, int index) {
            var battle = self.Battle;
            if (index == 0) {
                kicked = self.Target;
                if (kicked == null) return;
                var damage = self.MakeDamage(DamageType.Physical, self.RollCrit(), baseDmg, dmgMul);
                value = damage.Value;
                battle.PlayVisual(KickVisual, VisualMode.Burst, kicked.Position, kicked.Position, 0.8, 1);
                kicked.TakeDamage(damage, self);
                if (!kicked.Alive) {
                    kicked = null;
                    return;
                }
                var dx = kicked.Position.X - self.Position.X;
                var dy = kicked.Position.Y - self.Position.Y;
                var norm = Math.Sqrt(dx * dx + dy * dy);
                var away = norm < 1e-9 ? kicked.Position
                    : kicked.Position + new Vec2(dx / norm, dy / norm) * (distance * battle.Grid.AverageCellDistance);
                landing = battle.Displace(kicked, away, FlightTime);
                if (landing < 0) kicked.Airborne(airborne);
                return;
            }
            if (kicked == null || landing < 0) return;
            if (kicked.Alive) {
                kicked.Airborne(airborne);
                if (battle.IsEdgeCell(landing)) kicked.Stun(edgeStun);
            }
            battle.UnitsInRadius(self.Side.Opponent(), battle.Grid.Center(landing), 1, hits);
            battle.PlayVisual(LandVisual, VisualMode.Burst, battle.Grid.Center(landing), battle.Grid.Center(landing), 1, 1);
            var splash = new Damage(value * splashMul, DamageType.Physical, self.PhysicalPenetration);
            foreach (var u in hits) {
                if (u == kicked) continue;
                u.TakeDamage(splash, self);
                u.Airborne(airborne);
            }
            kicked = null;
            landing = -1;
        }
    }
}
