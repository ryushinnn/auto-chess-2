using System;
using System.Collections.Generic;
using System.IO;
using AutoChess.Core.Combat;

namespace AutoChess.Core.Tests {
    /// <summary>Small synthetic game data for rule tests, plus access to the real exported data.</summary>
    public static class TestData {
        public const string Brawler = "Brawler";     // cheap melee, Duelist / Mortal
        public const string Archer = "Archer";       // cheap ranged, Marksman / Mortal
        public const string Knight = "Knight";       // elite tank, Bruiser / Divine
        public const string Dragon = "Dragon";       // legendary, Sorcerer / Chaos
        public const string Dummy = "Dummy";         // summoned, never in a shop
        public const string Sword = "sword";         // component: +10 physical damage
        public const string Shield = "shield";       // component: +10 armor
        public const string Blade = "sword+sword";   // combined: +30% physical damage
        public const string Guard = "shield+sword";  // combined: +20 armor +20 physical damage
        public const string Wall = "shield+shield";  // combined: +50 armor

        public static UnitDefinition Unit(string id, Reputation rep, Role roles, Realm realm, float hp = 1000, float ad = 100,
            float armor = 20, float aspd = 1, float speed = 2, int range = 1) => new() {
            Id = id, Name = id, Reputation = rep, Roles = roles, Realm = realm, MaxHp = hp, PhysicalDamage = ad,
            MagicalDamage = 0, Armor = armor, Resistance = 20, AttackSpeed = aspd, MovementSpeed = speed,
            EnergyRegenPerAttack = 10, EnergyRegenPerHit = 5, AttackRange = range,
            Evolutions = new[] {
                Array.Empty<StatModifier>(),
                new[] { new StatModifier(StatKind.MaxHp, 0.8f, ModifierType.Percent), new StatModifier(StatKind.PhysicalDamage, 0.8f, ModifierType.Percent) },
                new[] { new StatModifier(StatKind.MaxHp, 3.5f, ModifierType.Percent), new StatModifier(StatKind.PhysicalDamage, 3.5f, ModifierType.Percent) },
            },
        };

        public static GameRules Rules() => new();

        public static GameDatabase Database(GameRules rules = null, StageDefinition[] stages = null) {
            var units = new List<UnitDefinition> {
                Unit(Brawler, Reputation.Unknown, Role.Duelist, Realm.Mortal),
                Unit(Archer, Reputation.Unknown, Role.Marksman, Realm.Mortal, hp: 700, ad: 120, range: 3),
                Unit(Knight, Reputation.Elite, Role.Bruiser, Realm.Divine, hp: 1800, ad: 70, armor: 60),
                Unit(Dragon, Reputation.Legendary, Role.Sorcerer, Realm.Chaos, hp: 2500, ad: 150),
                new() { Id = Dummy, Name = Dummy, Summoned = true, Moveable = false, MaxHp = 500, AttackSpeed = 1, MovementSpeed = 1 },
            };
            var items = new List<ItemDefinition> {
                new() { Id = Sword, Modifiers = new[] { new StatModifier(StatKind.PhysicalDamage, 10, ModifierType.Flat) } },
                new() { Id = Shield, Modifiers = new[] { new StatModifier(StatKind.Armor, 10, ModifierType.Flat) } },
                new() { Id = Blade, Ingredients = new[] { Sword, Sword }, Modifiers = new[] { new StatModifier(StatKind.PhysicalDamage, 0.3f, ModifierType.Percent) } },
                new() {
                    Id = Guard, Ingredients = new[] { Shield, Sword },
                    Modifiers = new[] { new StatModifier(StatKind.Armor, 20, ModifierType.Flat), new StatModifier(StatKind.PhysicalDamage, 20, ModifierType.Flat) }
                },
                new() { Id = Wall, Ingredients = new[] { Shield, Shield }, Modifiers = new[] { new StatModifier(StatKind.Armor, 50, ModifierType.Flat) } },
            };
            var traits = new List<TraitDefinition> {
                new() { Kind = TraitKind.Role, Role = Role.Marksman, Checkpoints = new[] { 2, 4, 6 }, Params = new[] { 0.1, 0.2, 0.3, 2, 3, 4.0 } },
                new() { Kind = TraitKind.Role, Role = Role.Duelist, Checkpoints = new[] { 2, 4 }, Params = Array.Empty<double>() },
                new() { Kind = TraitKind.Realm, Realm = Realm.Mortal, Checkpoints = new[] { 2, 4, 6, 8 }, Params = new double[10] },
            };
            return new GameDatabase(rules ?? Rules(), units, items, traits, stages ?? new[] { Stage() });
        }

        /// <summary>Two rounds: a single Dummy (easy win), then three Brawlers.</summary>
        public static StageDefinition Stage() => new() {
            Name = "Test Stage",
            Rounds = new[] {
                new RoundDefinition {
                    Enemies = new[] { new EnemyDefinition { UnitId = Dummy, Cell = new HexCoord(4, 3) } },
                    Rewards = new[] { new RewardDefinition { Kind = RewardKind.Coin, Coins = 5 } },
                    WinRewards = new[] { new RewardDefinition { Kind = RewardKind.RawItem } },
                },
                new RoundDefinition {
                    Enemies = new[] {
                        new EnemyDefinition { UnitId = Brawler, Cell = new HexCoord(5, 2) },
                        new EnemyDefinition { UnitId = Brawler, Cell = new HexCoord(5, 4) },
                        new EnemyDefinition { UnitId = Brawler, Cell = new HexCoord(6, 3) },
                    },
                    Rewards = new[] { new RewardDefinition { Kind = RewardKind.Coin, Coins = 3 } },
                },
            },
        };

        /// <summary>Plain melee / ranged auto-attacks for the synthetic units.</summary>
        public static AbilityRegistry Abilities() {
            var r = new AbilityRegistry();
            r.Register(Brawler, _ => new BasicAttack(1f, 0.3f, DamageType.Physical), null);
            r.Register(Archer, _ => new BasicAttack(1f, 0.3f, DamageType.Physical), null);
            r.Register(Knight, _ => new BasicAttack(1f, 0.3f, DamageType.Physical), null);
            r.Register(Dragon, _ => new BasicAttack(1f, 0.3f, DamageType.Physical), null);
            return r;
        }

        public static MatchContext Context(GameDatabase db = null, ulong seed = 7) {
            db ??= Database();
            var state = new MatchState();
            var ctx = new MatchContext(db, new DeterministicRandom(seed), new EventBuffer(), new UnlimitedUnitPool(db)) { State = state };
            return ctx;
        }

        public static PlayerState Player(MatchContext ctx, int gold = 100, ulong seed = 11) {
            var p = new PlayerState(ctx.State.Players.Count, ctx.Rules) { Gold = gold, Rng = new DeterministicRandom(seed) };
            ctx.State.Players.Add(p);
            return p;
        }

        public static LocalGameSession Session(GameDatabase db = null, ulong seed = 3, bool skipStarter = true, bool cheats = true) {
            var session = new LocalGameSession(db ?? Database(), new MatchConfig {
                Seed = seed, AllowCheats = cheats, SkipStarterPack = skipStarter, Abilities = Abilities()
            });
            session.Start();
            return session;
        }

        // ------------------------------------------------------------------ real data

        /// <summary>
        /// Path of the game data exported from the ScriptableObjects (menu AutoChess/Export Game Data), or null.
        /// Looked up from the working directory and the test assembly upwards.
        /// </summary>
        public static string RealDataPath() {
            var env = Environment.GetEnvironmentVariable("AUTOCHESS_GAME_DATA");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }) {
                var dir = new DirectoryInfo(start);
                for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent) {
                    var candidate = Path.Combine(dir.FullName, "GameData", "game_data.json");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }
    }
}
