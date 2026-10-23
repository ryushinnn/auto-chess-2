using System;
using System.Collections.Generic;

namespace AutoChess.Core.Combat {
    /// <summary>Battle effect of an active synergy (was DestinyProcessor).</summary>
    public abstract class TraitEffect {
        public abstract void Apply(BattleSimulation battle, TeamSide side, TraitDefinition def, int tier);

        protected static List<CombatUnit> Members(BattleSimulation battle, TeamSide side, Role role) {
            var list = new List<CombatUnit>();
            foreach (var u in battle.AliveUnits(side)) {
                if (u.Def.Roles.HasRole(role)) list.Add(u);
            }
            return list;
        }

        protected static List<CombatUnit> Members(BattleSimulation battle, TeamSide side, Realm realm) {
            var list = new List<CombatUnit>();
            foreach (var u in battle.AliveUnits(side)) {
                if (u.Def.Realm == realm) list.Add(u);
            }
            return list;
        }

        /// <summary>"The strongest" unit: higher rank, then higher tier, then more health (deterministic).</summary>
        protected static double Power(CombatUnit u) => (int)u.Rank * 1e6 + (int)u.Def.Reputation * 1e4 + u.MaxHp;

        protected static CombatUnit Strongest(IEnumerable<CombatUnit> units, Func<CombatUnit, bool> filter = null) {
            CombatUnit best = null;
            foreach (var u in units) {
                if (!u.Alive || (filter != null && !filter(u))) continue;
                if (best == null || Power(u) > Power(best)) best = u;
            }
            return best;
        }

        protected static void Bonus(CombatUnit u, string key, params StatModifier[] modifiers) =>
            u.AddModifierSet(ModifierSet.Permanent(u, key, modifiers, mark: false));
    }

    /// <summary><see cref="IHitHook"/> from two delegates.</summary>
    public sealed class HitHook : IHitHook {
        public delegate bool Before(CombatUnit target, CombatUnit source, ref Damage damage, HitKind kind);

        public delegate void After(CombatUnit target, CombatUnit source, Damage damage, double dealt, HitKind kind);

        readonly Before before;
        readonly After after;

        public HitHook(Before before = null, After after = null) {
            this.before = before;
            this.after = after;
        }

        public bool BeforeHit(CombatUnit target, CombatUnit source, ref Damage damage, HitKind kind) =>
            before == null || before(target, source, ref damage, kind);

        public void AfterHit(CombatUnit target, CombatUnit source, Damage damage, double dealt, HitKind kind) =>
            after?.Invoke(target, source, damage, dealt, kind);
    }

    /// <summary>
    /// Synergy effects, applied when a battle starts. Parameter layout of every trait = its asset's
    /// destinyParams (Data/Destinies), in order; tier = index of the reached checkpoint.
    /// </summary>
    public static class TraitEffects {
        static readonly Dictionary<TraitId, Func<TraitEffect>> Factories = new() {
            { TraitId.Of(Role.Assassin), () => new AssassinTrait() },
            { TraitId.Of(Role.Bruiser), () => new BruiserTrait() },
            { TraitId.Of(Role.Cultist), () => new CultistTrait() },
            { TraitId.Of(Role.Duelist), () => new DuelistTrait() },
            { TraitId.Of(Role.Marksman), () => new MarksmanTrait() },
            { TraitId.Of(Role.Sorcerer), () => new SorcererTrait() },
            { TraitId.Of(Realm.Chaos), () => new ChaosTrait() },
            { TraitId.Of(Realm.Divine), () => new DivineTrait() },
            { TraitId.Of(Realm.Mecha), () => new MechaTrait() },
            { TraitId.Of(Realm.Mortal), () => new MortalTrait() },
            { TraitId.Of(Realm.Nether), () => new NetherTrait() },
        };

        public static void Register(TraitId id, Func<TraitEffect> factory) => Factories[id] = factory;

        /// <summary>Activates every reached synergy of a team, counted over its distinct unit definitions.</summary>
        public static void Apply(BattleSimulation battle, TeamSide side) {
            var ids = new List<string>();
            foreach (var u in battle.AliveUnits(side)) ids.Add(u.Def.Id);
            var disabled = battle.Setup.Team(side).DisabledTraits;
            foreach (var status in Traits.Compute(battle.Db, ids)) {
                if (!status.Active || disabled.Contains(status.Id) || !Factories.TryGetValue(status.Id, out var factory)) continue;
                var def = battle.Db.Trait(status.Id);
                if (def != null) factory().Apply(battle, side, def, status.Tier);
            }
        }
    }

    // ---------------------------------------------------------------------- roles

    /// <summary>
    /// Assassins gain crit chance, and their crits may turn into true damage.
    /// Params: [0..2] crit chance per tier, [3..5] chance that a crit becomes true damage.
    /// </summary>
    public sealed class AssassinTrait : TraitEffect {
        public override void Apply(BattleSimulation battle, TeamSide side, TraitDefinition def, int tier) {
            var assassins = Members(battle, side, Role.Assassin);
            foreach (var u in assassins) Bonus(u, "assassin", new StatModifier(StatKind.CriticalChance, def.Param(tier), ModifierType.Flat));
            var chance = def.Param(3 + tier);
            battle.AddHitHook(new HitHook(before: (CombatUnit target, CombatUnit source, ref Damage damage, HitKind kind) => {
                if (damage.Crit && damage.Type != DamageType.True && source != null && assassins.Contains(source)
                    && battle.Rng.Chance(chance)) {
                    damage.Type = DamageType.True;
                    damage.Penetration = 0;
                }
                return true;
            }));
        }
    }

    /// <summary>
    /// Bruisers gain health, and armor + resistance while above a health threshold.
    /// Params: [0] health threshold, [1..3] health per tier, [4..6] armor / resistance per tier.
    /// </summary>
    public sealed class BruiserTrait : TraitEffect {
        const string Guard = "bruiser_guard";

        public override void Apply(BattleSimulation battle, TeamSide side, TraitDefinition def, int tier) {
            var bruisers = Members(battle, side, Role.Bruiser);
            var threshold = def.Param(0);
            var defense = def.Param(4 + tier);
            foreach (var u in bruisers) Bonus(u, "bruiser", new StatModifier(StatKind.MaxHp, def.Param(1 + tier), ModifierType.Flat));
            battle.AddTicker(_ => {
                foreach (var u in bruisers) {
                    if (!u.Alive) continue;
                    var want = u.Hp > u.MaxHp * threshold;
                    if (want == u.HasModifierSet(Guard, u)) continue;
                    if (want) {
                        u.AddModifierSet(ModifierSet.Permanent(u, Guard, new[] {
                            new StatModifier(StatKind.Armor, defense, ModifierType.Flat),
                            new StatModifier(StatKind.Resistance, defense, ModifierType.Flat),
                        }));
                    }
                    else u.RemoveModifierSet(Guard, u);
                }
            });
        }
    }

    /// <summary>
    /// The strongest Cultist hosts the DEMON LORD: health, both damage types, lifesteal, plus a boon of its
    /// own role (each cultist is a different kind of demon). When the host dies the strongest surviving
    /// Cultist hosts it again one tier lower; at the lowest tier it does not come back.
    /// Params: [0..2] health per tier, [3..5] physical + magical damage per tier, [6..8] lifesteal per tier.
    /// </summary>
    public sealed class CultistTrait : TraitEffect {
        public const string Key = "demon_lord";

        public override void Apply(BattleSimulation battle, TeamSide side, TraitDefinition def, int tier) {
            var cultists = Members(battle, side, Role.Cultist);
            var host = Strongest(cultists);
            var hostTier = tier;
            if (host == null) return;
            Possess(host, def, hostTier);
            battle.UnitDied += (dead, _) => {
                if (dead != host || hostTier <= 0) return;
                host = Strongest(cultists);
                if (host == null) return;
                Possess(host, def, --hostTier);
            };
        }

        static void Possess(CombatUnit host, TraitDefinition def, int tier) {
            var mods = new List<StatModifier> {
                new(StatKind.MaxHp, def.Param(tier), ModifierType.Flat),
                new(StatKind.PhysicalDamage, def.Param(3 + tier), ModifierType.Flat),
                new(StatKind.MagicalDamage, def.Param(3 + tier), ModifierType.Flat),
                new(StatKind.LifeSteal, def.Param(6 + tier), ModifierType.Flat),
            };
            mods.AddRange(Boon(host.Def.Roles));
            host.AddModifierSet(ModifierSet.Permanent(host, Key, mods.ToArray(), stacks: tier + 1, visual: Key));
            if (host.Def.Roles.HasRole(Role.Sorcerer)) host.FillEnergy();
        }

        /// <summary>What the demon adds on top, by the host's other role.</summary>
        public static StatModifier[] Boon(Role roles) {
            if (roles.HasRole(Role.Sorcerer)) return new[] { new StatModifier(StatKind.EnergyRegenEfficiency, 0.4, ModifierType.Flat) };
            if (roles.HasRole(Role.Duelist)) return new[] { new StatModifier(StatKind.AttackSpeed, 0.3, ModifierType.Percent) };
            if (roles.HasRole(Role.Assassin)) return new[] { new StatModifier(StatKind.CriticalDamage, 0.4, ModifierType.Flat) };
            if (roles.HasRole(Role.Marksman)) return new[] { new StatModifier(StatKind.CriticalChance, 0.25, ModifierType.Flat) };
            if (roles.HasRole(Role.Bruiser)) {
                return new[] {
                    new StatModifier(StatKind.Armor, 40, ModifierType.Flat), new StatModifier(StatKind.Resistance, 40, ModifierType.Flat)
                };
            }
            return new[] { new StatModifier(StatKind.MaxHp, 0.2, ModifierType.Percent) };
        }
    }

    /// <summary>
    /// Duelists gain physical damage and may dodge auto-attacks (not crits, not true damage).
    /// Params: [0] (unused health threshold), [1..3] physical damage per tier, [4..6] dodge chance per tier.
    /// </summary>
    public sealed class DuelistTrait : TraitEffect {
        public override void Apply(BattleSimulation battle, TeamSide side, TraitDefinition def, int tier) {
            var duelists = Members(battle, side, Role.Duelist);
            foreach (var u in duelists) Bonus(u, "duelist", new StatModifier(StatKind.PhysicalDamage, def.Param(1 + tier), ModifierType.Flat));
            var dodge = def.Param(4 + tier);
            battle.AddHitHook(new HitHook(before: (CombatUnit target, CombatUnit source, ref Damage damage, HitKind kind) =>
                kind != HitKind.Attack || damage.Crit || damage.Type == DamageType.True || !duelists.Contains(target)
                || !battle.Rng.Chance(dodge)));
        }
    }

    /// <summary>
    /// Marksmen gain attack speed; when one dies the surviving marksmen get the same bonus again for a few
    /// seconds. Params: [0..2] attack speed per tier, [3..5] burst duration per tier.
    /// </summary>
    public sealed class MarksmanTrait : TraitEffect {
        public override void Apply(BattleSimulation battle, TeamSide side, TraitDefinition def, int tier) {
            var bonus = def.Param(tier);
            var burst = def.Param(3 + tier);
            var marksmen = Members(battle, side, Role.Marksman);
            foreach (var u in marksmen) {
                u.AddModifierSet(ModifierSet.Permanent(u, "marksman",
                    new[] { new StatModifier(StatKind.AttackSpeed, bonus, ModifierType.Percent) }, mark: false));
            }

            battle.UnitDied += (dead, _) => {
                if (!marksmen.Contains(dead)) return;
                foreach (var other in marksmen) {
                    if (other == dead || !other.Alive) continue;
                    other.AddModifierSet(ModifierSet.Timed(dead, "marksman_burst", burst,
                        new[] { new StatModifier(StatKind.AttackSpeed, bonus, ModifierType.Percent) }, mark: false));
                }
            };
        }
    }

    /// <summary>
    /// Sorcerers gain magic damage; after its first cast a sorcerer may cast its skill once more right away.
    /// Params: [0..2] magic damage per tier, [3..5] chance of the echo cast per tier.
    /// </summary>
    public sealed class SorcererTrait : TraitEffect {
        public override void Apply(BattleSimulation battle, TeamSide side, TraitDefinition def, int tier) {
            var sorcerers = Members(battle, side, Role.Sorcerer);
            foreach (var u in sorcerers) Bonus(u, "sorcerer", new StatModifier(StatKind.MagicalDamage, def.Param(tier), ModifierType.Flat));
            var chance = def.Param(3 + tier);
            var casted = new List<CombatUnit>();
            battle.SkillCompleted += u => {
                if (!sorcerers.Contains(u) || casted.Contains(u)) return;
                casted.Add(u);
                if (battle.Rng.Chance(chance)) u.FillEnergy();
            };
        }
    }

    // ---------------------------------------------------------------------- realms

    /// <summary>
    /// With both Aatrox halves on the team, the Apocalypse Warrior (Aatrox_TrueForm) joins the fight: health and
    /// damages = sum of the strongest Dark and Light, armor / resistance / crit = their average, penetrations =
    /// the higher one, plus bonus health. Every few seconds it heals part of its missing health and becomes
    /// invulnerable for a moment. Params: [0] bonus health, [1] interval, [2] heal (missing health), [3] invulnerability.
    /// </summary>
    public sealed class ChaosTrait : TraitEffect {
        public override void Apply(BattleSimulation battle, TeamSide side, TraitDefinition def, int tier) {
            var members = Members(battle, side, Realm.Chaos);
            var dark = Strongest(members, u => u.Def.Id == HeroIds.AatroxDark);
            var light = Strongest(members, u => u.Def.Id == HeroIds.AatroxLight);
            if (dark == null || light == null || !battle.Db.TryGetUnit(HeroIds.AatroxTrueForm, out _)) return;

            var warrior = battle.Summon(HeroIds.AatroxTrueForm, side, (dark.Position + light.Position) * 0.5);
            if (warrior == null) return;
            var mods = new List<StatModifier>();
            void Set(StatKind stat, double value) {
                var delta = value - warrior[stat];
                if (Math.Abs(delta) > 1e-9) mods.Add(new StatModifier(stat, delta, ModifierType.Flat));
            }
            Set(StatKind.MaxHp, dark.MaxHp + light.MaxHp + def.Param(0));
            Set(StatKind.PhysicalDamage, dark.PhysicalDamage + light.PhysicalDamage);
            Set(StatKind.MagicalDamage, dark.MagicalDamage + light.MagicalDamage);
            Set(StatKind.Armor, (dark.Armor + light.Armor) / 2);
            Set(StatKind.Resistance, (dark.Resistance + light.Resistance) / 2);
            Set(StatKind.CriticalChance, (dark.CriticalChance + light.CriticalChance) / 2);
            Set(StatKind.CriticalDamage, (dark.CriticalDamage + light.CriticalDamage) / 2);
            Set(StatKind.PhysicalPenetration, Math.Max(dark.PhysicalPenetration, light.PhysicalPenetration));
            Set(StatKind.MagicalPenetration, Math.Max(dark.MagicalPenetration, light.MagicalPenetration));
            warrior.AddModifierSet(ModifierSet.Permanent(warrior, "chaos_form", mods.ToArray(), mark: false));

            var interval = def.Param(1);
            var heal = def.Param(2);
            var shield = def.Param(3);
            var timer = 0d;
            battle.AddTicker(dt => {
                if (!warrior.Alive || interval <= 0) return;
                timer += dt;
                if (timer < interval) return;
                timer -= interval;
                warrior.Heal((warrior.MaxHp - warrior.Hp) * heal);
                warrior.MakeInvulnerable(shield);
            });
        }
    }

    /// <summary>
    /// Divine auto-attacks may stun; crits and true damage stun more often and longer. From the third tier
    /// divine units deal bonus damage to stunned enemies. Params: [0..3] stun chance per tier, [4] stun
    /// duration, [5] chance multiplier and [6] extra duration on crit / true damage, [7..8] bonus damage
    /// at tiers 3 and 4.
    /// </summary>
    public sealed class DivineTrait : TraitEffect {
        public override void Apply(BattleSimulation battle, TeamSide side, TraitDefinition def, int tier) {
            var divines = Members(battle, side, Realm.Divine);
            var chance = def.Param(tier);
            var duration = def.Param(4);
            var strongMul = def.Param(5);
            var strongDuration = def.Param(6);
            var bonus = tier >= 2 ? def.Param(7 + tier - 2) : 0;
            battle.AddHitHook(new HitHook(
                (CombatUnit target, CombatUnit source, ref Damage damage, HitKind kind) => {
                    if (bonus > 0 && target.Status.IsStunned && source != null && divines.Contains(source)) damage.Value *= 1 + bonus;
                    return true;
                },
                (target, source, damage, dealt, kind) => {
                    if (kind != HitKind.Attack || !target.Alive || source == null || !divines.Contains(source)) return;
                    var strong = damage.Crit || damage.Type == DamageType.True;
                    if (battle.Rng.Chance(strong ? chance * strongMul : chance)) {
                        target.Stun(strong ? duration * (1 + strongDuration) : duration);
                    }
                }));
        }
    }

    /// <summary>
    /// Every so much damage a Mecha deals or takes, the SQUADRON fires a rocket at the enemy with the lowest
    /// health: physical or magic damage, whichever its defenses resist less; later tiers lower the thresholds,
    /// raise the damage, and finally make the rockets true damage that can crit. Params: [0] damage dealt per
    /// request, [1] damage taken per request, [2] rocket damage, [3] threshold reduction (tier 2+), [4] rocket
    /// bonus damage (tier 3+), [5] crit chance and [6] crit damage (tier 4).
    /// </summary>
    public sealed class MechaTrait : TraitEffect {
        public override void Apply(BattleSimulation battle, TeamSide side, TraitDefinition def, int tier) {
            var mechas = Members(battle, side, Realm.Mecha);
            var reduce = tier >= 1 ? 1 - def.Param(3) : 1;
            var dealThreshold = def.Param(0) * reduce;
            var takeThreshold = def.Param(1) * reduce;
            var rocket = def.Param(2) * (tier >= 2 ? 1 + def.Param(4) : 1);
            var trueDamage = tier >= 3;
            var critChance = def.Param(5);
            var critDamage = def.Param(6);
            var dealt = new double[mechas.Count];
            var taken = new double[mechas.Count];
            var firing = false;

            void Fire(CombatUnit requester) {
                CombatUnit target = null;
                foreach (var e in battle.AliveUnits(side.Opponent())) {
                    if (target == null || e.Hp < target.Hp) target = e;
                }
                if (target == null) return;
                Damage damage;
                if (trueDamage) {
                    var crit = battle.Rng.Chance(critChance);
                    damage = new Damage(crit ? rocket * critDamage : rocket, DamageType.True, 0, crit);
                }
                else {
                    damage = new Damage(rocket, target.Armor <= target.Resistance ? DamageType.Physical : DamageType.Magical, 0);
                }
                firing = true;
                using (battle.Hit(HitKind.Effect)) target.TakeDamage(damage, requester, false);
                firing = false;
            }

            battle.AddHitHook(new HitHook(after: (target, source, damage, amount, kind) => {
                if (firing || amount <= 0) return;
                var s = source == null ? -1 : mechas.IndexOf(source);
                if (s >= 0 && dealThreshold > 0) {
                    dealt[s] += amount;
                    while (dealt[s] >= dealThreshold && !battle.Finished) {
                        dealt[s] -= dealThreshold;
                        Fire(source);
                    }
                }
                var t = mechas.IndexOf(target);
                if (t >= 0 && takeThreshold > 0) {
                    taken[t] += amount;
                    while (taken[t] >= takeThreshold && !battle.Finished) {
                        taken[t] -= takeThreshold;
                        Fire(target);
                    }
                }
            }));
        }
    }

    /// <summary>
    /// Every Mortal hero on the board grants its own stat to itself and every other Mortal; more Mortals raise
    /// the amount. Params (asset order): [0..5] the stat of Akali (magic damage), Jinx (physical damage), Caitlyn
    /// (energy regen), Katarina (crit chance), Tristana (crit damage), Miss Fortune (penetration), [6..9] the
    /// multiplier per tier, then at the top tier [10] tenacity and [11] armor / resistance for every Mortal.
    /// </summary>
    public sealed class MortalTrait : TraitEffect {
        public override void Apply(BattleSimulation battle, TeamSide side, TraitDefinition def, int tier) {
            var mortals = new List<CombatUnit>();
            foreach (var u in battle.AliveUnits(side)) {
                if (u.Def.Realm == Realm.Mortal) mortals.Add(u);
            }
            var mul = def.Param(6 + tier);
            var mods = new List<StatModifier>();
            var granted = new List<string>();
            foreach (var hero in mortals) {
                if (granted.Contains(hero.Def.Id)) continue;
                granted.Add(hero.Def.Id);
                switch (hero.Def.Id) {
                    case HeroIds.Akali:
                        mods.Add(new StatModifier(StatKind.MagicalDamage, def.Param(0) * mul, ModifierType.Flat));
                        break;
                    case HeroIds.Jinx:
                        mods.Add(new StatModifier(StatKind.PhysicalDamage, def.Param(1) * mul, ModifierType.Flat));
                        break;
                    case HeroIds.Caitlyn:
                        mods.Add(new StatModifier(StatKind.EnergyRegenEfficiency, def.Param(2) * mul, ModifierType.Flat));
                        break;
                    case HeroIds.Katarina:
                        mods.Add(new StatModifier(StatKind.CriticalChance, def.Param(3) * mul, ModifierType.Flat));
                        break;
                    case HeroIds.Tristana:
                        mods.Add(new StatModifier(StatKind.CriticalDamage, def.Param(4) * mul, ModifierType.Flat));
                        break;
                    case HeroIds.MissFortune:
                        mods.Add(new StatModifier(StatKind.MagicalPenetration, def.Param(5) * mul, ModifierType.Flat));
                        mods.Add(new StatModifier(StatKind.PhysicalPenetration, def.Param(5) * mul, ModifierType.Flat));
                        break;
                }
            }
            if (tier >= 3) {
                mods.Add(new StatModifier(StatKind.Tenacity, def.Param(10), ModifierType.Flat));
                mods.Add(new StatModifier(StatKind.Armor, def.Param(11), ModifierType.Flat));
                mods.Add(new StatModifier(StatKind.Resistance, def.Param(11), ModifierType.Flat));
            }
            if (mods.Count == 0) return;
            foreach (var hero in mortals) hero.AddModifierSet(ModifierSet.Permanent(hero, "mortal", mods.ToArray(), mark: false));
        }
    }

    /// <summary>
    /// Nether units turn part of their damage into decaying BLOOD DEBT on the enemy. An enemy whose health falls
    /// below its debt is executed, and part of the debt heals the living Nether units (split evenly). At the top
    /// tier the debt no longer decays. Params: [0..3] damage to debt per tier, [4] debt turned into healing.
    /// </summary>
    public sealed class NetherTrait : TraitEffect {
        public const string Key = "blood_debt";
        /// <summary>Share of the debt lost per second below the top tier (the description leaves it open).</summary>
        public const double DecayPerSecond = 0.15;

        public override void Apply(BattleSimulation battle, TeamSide side, TraitDefinition def, int tier) {
            var nether = Members(battle, side, Realm.Nether);
            var conversion = def.Param(tier);
            var healShare = def.Param(4);
            var decays = tier < 3;
            var debt = new Dictionary<int, double>();
            var executing = false;

            void Show(CombatUnit target, double amount) {
                var stacks = (int)Math.Round(amount);
                var mark = target.GetMark(Key, null);
                if (stacks <= 0) {
                    if (mark != null) target.RemoveMark(mark);
                    return;
                }
                if (mark != null && mark.Stacks == stacks) return;
                target.AddMark(new Mark { Key = Key, Stacks = stacks, Duration = double.PositiveInfinity });
            }

            void CheckExecution(CombatUnit target, CombatUnit executor) {
                if (executing || !target.Alive || !debt.TryGetValue(target.Id, out var amount) || target.Hp > amount) return;
                debt.Remove(target.Id);
                Show(target, 0);
                executing = true;
                using (battle.Hit(HitKind.Effect)) target.TakeDamage(new Damage(target.Hp + 1, DamageType.True, 0), executor, false);
                executing = false;
                var living = new List<CombatUnit>();
                foreach (var n in nether) {
                    if (n.Alive) living.Add(n);
                }
                foreach (var n in living) n.Heal(amount * healShare / living.Count);
            }

            battle.AddHitHook(new HitHook(after: (target, source, damage, dealt, kind) => {
                if (executing || target.Side == side) return;
                if (source != null && dealt > 0 && nether.Contains(source) && target.Alive) {
                    debt.TryGetValue(target.Id, out var amount);
                    debt[target.Id] = amount + dealt * conversion;
                    Show(target, debt[target.Id]);
                }
                if (debt.ContainsKey(target.Id)) CheckExecution(target, source != null && nether.Contains(source) ? source : null);
            }));

            if (!decays) return;
            battle.AddTicker(dt => {
                // Units in battle order (dictionary order must never drive the simulation).
                foreach (var u in battle.Units) {
                    if (!debt.TryGetValue(u.Id, out var amount)) continue;
                    if (!u.Alive) {
                        debt.Remove(u.Id);
                        continue;
                    }
                    amount *= 1 - DecayPerSecond * dt;
                    if (amount < 1) {
                        debt.Remove(u.Id);
                        Show(u, 0);
                        continue;
                    }
                    debt[u.Id] = amount;
                    Show(u, amount);
                }
            });
        }
    }
}
