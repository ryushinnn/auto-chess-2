using System;
using System.Collections.Generic;
using System.Globalization;

namespace AutoChess.Core {
    /// <summary>
    /// Engine-free exchange format of the game data (units, items, traits, stages, rules). The client exports
    /// it from the ScriptableObject assets (menu AutoChess/Export Game Data); a dedicated server or the
    /// offline test runner loads it with <see cref="Load"/>.
    /// </summary>
    public static class GameDataJson {
        public const int FormatVersion = 1;

        public sealed class Content {
            public GameRules Rules = new();
            public List<UnitDefinition> Units = new();
            public List<ItemDefinition> Items = new();
            public List<TraitDefinition> Traits = new();
            public List<StageDefinition> Stages = new();

            public GameDatabase ToDatabase() => new(Rules, Units, Items, Traits, Stages);
        }

        public static GameDatabase Load(string json) => Read(json).ToDatabase();

        // ------------------------------------------------------------------ write

        public static string Write(Content c) {
            var root = new Dictionary<string, object> {
                ["format"] = FormatVersion,
                ["rules"] = new Dictionary<string, object> {
                    ["cellWidth"] = c.Rules.CellWidth,
                    ["cellHeight"] = c.Rules.CellHeight,
                },
                ["units"] = Map(c.Units, WriteUnit),
                ["items"] = Map(c.Items, i => new Dictionary<string, object> {
                    ["id"] = i.Id, ["name"] = i.Name, ["ingredients"] = i.Ingredients, ["modifiers"] = Map(i.Modifiers, WriteModifier),
                }),
                ["traits"] = Map(c.Traits, t => new Dictionary<string, object> {
                    ["kind"] = t.Kind.ToString(),
                    ["value"] = t.Kind == TraitKind.Role ? t.Role.ToString() : t.Realm.ToString(),
                    ["name"] = t.Name, ["checkpoints"] = t.Checkpoints, ["params"] = t.Params,
                }),
                ["stages"] = Map(c.Stages, s => new Dictionary<string, object> {
                    ["name"] = s.Name,
                    ["rounds"] = Map(s.Rounds, r => new Dictionary<string, object> {
                        ["enemies"] = Map(r.Enemies, e => new Dictionary<string, object> {
                            ["unit"] = e.UnitId, ["rank"] = e.Rank.ToString(), ["row"] = e.Cell.Row, ["col"] = e.Cell.Col,
                            ["items"] = e.Items, ["hpScale"] = e.HpScale, ["damageScale"] = e.DamageScale,
                        }),
                        ["rewards"] = Map(r.Rewards, WriteReward),
                        ["winRewards"] = Map(r.WinRewards, WriteReward),
                    }),
                }),
            };
            return MiniJson.Serialize(root);
        }

        static List<object> Map<T>(IEnumerable<T> items, Func<T, object> f) {
            var list = new List<object>();
            foreach (var x in items) list.Add(f(x));
            return list;
        }

        static object WriteModifier(StatModifier m) => new Dictionary<string, object> {
            ["stat"] = m.Stat.ToString(), ["value"] = m.Value, ["type"] = m.Type.ToString(),
        };

        static object WriteUnit(UnitDefinition u) {
            var evolutions = new List<object>();
            foreach (var e in u.Evolutions) evolutions.Add(Map(e ?? Array.Empty<StatModifier>(), WriteModifier));
            return new Dictionary<string, object> {
                ["id"] = u.Id, ["name"] = u.Name, ["summoned"] = u.Summoned, ["realm"] = u.Realm.ToString(),
                ["roles"] = (int)u.Roles, ["reputation"] = u.Reputation.ToString(), ["moveable"] = u.Moveable,
                ["maxHp"] = u.MaxHp, ["physicalDamage"] = u.PhysicalDamage, ["magicalDamage"] = u.MagicalDamage,
                ["armor"] = u.Armor, ["resistance"] = u.Resistance, ["attackSpeed"] = u.AttackSpeed,
                ["movementSpeed"] = u.MovementSpeed, ["energyPerAttack"] = u.EnergyRegenPerAttack,
                ["energyPerHit"] = u.EnergyRegenPerHit, ["startingEnergy"] = u.StartingEnergy, ["attackRange"] = u.AttackRange,
                ["unstoppable"] = u.Unstoppable,
                ["evolutions"] = evolutions, ["skillParams"] = u.SkillParams, ["skillParamKeys"] = u.SkillParamKeys,
                ["specialKeys"] = u.SpecialKeys,
            };
        }

        static object WriteReward(RewardDefinition r) => new Dictionary<string, object> {
            ["kind"] = r.Kind.ToString(), ["coins"] = r.Coins, ["minCoins"] = r.MinCoins, ["maxCoins"] = r.MaxCoins,
            ["reputation"] = r.Reputation.ToString(),
            ["options"] = Map(r.Options, o => new Dictionary<string, object> { ["rate"] = o.Rate, ["reward"] = WriteReward(o.Reward) }),
        };

        // ------------------------------------------------------------------ read

        public static Content Read(string json) {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root)) throw new FormatException("game data: root is not an object");
            var c = new Content();
            if (root.TryGetValue("rules", out var rulesObj) && rulesObj is Dictionary<string, object> rules) {
                c.Rules.CellWidth = F(rules, "cellWidth", c.Rules.CellWidth);
                c.Rules.CellHeight = F(rules, "cellHeight", c.Rules.CellHeight);
            }
            foreach (var o in List(root, "units")) c.Units.Add(ReadUnit((Dictionary<string, object>)o));
            foreach (var o in List(root, "items")) {
                var d = (Dictionary<string, object>)o;
                c.Items.Add(new ItemDefinition {
                    Id = S(d, "id"), Name = S(d, "name"), Ingredients = Strings(d, "ingredients"), Modifiers = Modifiers(List(d, "modifiers")),
                });
            }
            foreach (var o in List(root, "traits")) {
                var d = (Dictionary<string, object>)o;
                var kind = E<TraitKind>(d, "kind");
                var t = new TraitDefinition { Kind = kind, Name = S(d, "name"), Checkpoints = Ints(d, "checkpoints"), Params = Floats(d, "params") };
                if (kind == TraitKind.Role) t.Role = E<Role>(d, "value");
                else t.Realm = E<Realm>(d, "value");
                c.Traits.Add(t);
            }
            foreach (var o in List(root, "stages")) {
                var d = (Dictionary<string, object>)o;
                var rounds = new List<RoundDefinition>();
                foreach (var ro in List(d, "rounds")) {
                    var rd = (Dictionary<string, object>)ro;
                    var enemies = new List<EnemyDefinition>();
                    foreach (var eo in List(rd, "enemies")) {
                        var ed = (Dictionary<string, object>)eo;
                        enemies.Add(new EnemyDefinition {
                            UnitId = S(ed, "unit"), Rank = E<HeroRank>(ed, "rank"),
                            Cell = new HexCoord((int)F(ed, "row", 0), (int)F(ed, "col", 0)), Items = Strings(ed, "items"),
                            HpScale = F(ed, "hpScale", 1), DamageScale = F(ed, "damageScale", 1),
                        });
                    }
                    rounds.Add(new RoundDefinition {
                        Enemies = enemies.ToArray(), Rewards = Rewards(List(rd, "rewards")), WinRewards = Rewards(List(rd, "winRewards")),
                    });
                }
                c.Stages.Add(new StageDefinition { Name = S(d, "name"), Rounds = rounds.ToArray() });
            }
            return c;
        }

        static UnitDefinition ReadUnit(Dictionary<string, object> d) {
            var evolutions = new List<StatModifier[]>();
            foreach (var e in List(d, "evolutions")) evolutions.Add(Modifiers((List<object>)e));
            return new UnitDefinition {
                Id = S(d, "id"), Name = S(d, "name"), Summoned = B(d, "summoned"), Realm = E<Realm>(d, "realm"),
                Roles = (Role)(int)F(d, "roles", 0), Reputation = E<Reputation>(d, "reputation"), Moveable = B(d, "moveable", true),
                MaxHp = F(d, "maxHp", 0), PhysicalDamage = F(d, "physicalDamage", 0), MagicalDamage = F(d, "magicalDamage", 0),
                Armor = F(d, "armor", 0), Resistance = F(d, "resistance", 0), AttackSpeed = F(d, "attackSpeed", 1),
                MovementSpeed = F(d, "movementSpeed", 1), EnergyRegenPerAttack = F(d, "energyPerAttack", 0),
                EnergyRegenPerHit = F(d, "energyPerHit", 0), StartingEnergy = F(d, "startingEnergy", 0),
                AttackRange = (int)F(d, "attackRange", 1),
                Unstoppable = B(d, "unstoppable"), Evolutions = evolutions.ToArray(), SkillParams = Floats(d, "skillParams"),
                SkillParamKeys = Strings(d, "skillParamKeys"), SpecialKeys = Strings(d, "specialKeys"),
            };
        }

        static RewardDefinition[] Rewards(List<object> list) {
            var result = new RewardDefinition[list.Count];
            for (var i = 0; i < list.Count; i++) result[i] = ReadReward((Dictionary<string, object>)list[i]);
            return result;
        }

        static RewardDefinition ReadReward(Dictionary<string, object> d) {
            var options = new List<WeightedReward>();
            foreach (var o in List(d, "options")) {
                var od = (Dictionary<string, object>)o;
                options.Add(new WeightedReward { Rate = (int)F(od, "rate", 0), Reward = ReadReward((Dictionary<string, object>)od["reward"]) });
            }
            return new RewardDefinition {
                Kind = E<RewardKind>(d, "kind"), Coins = (int)F(d, "coins", 0), MinCoins = (int)F(d, "minCoins", 0),
                MaxCoins = (int)F(d, "maxCoins", 0), Reputation = E<Reputation>(d, "reputation"), Options = options.ToArray(),
            };
        }

        static StatModifier[] Modifiers(List<object> list) {
            var result = new StatModifier[list.Count];
            for (var i = 0; i < list.Count; i++) {
                var d = (Dictionary<string, object>)list[i];
                result[i] = new StatModifier(E<StatKind>(d, "stat"), F(d, "value", 0), E<ModifierType>(d, "type"));
            }
            return result;
        }

        static List<object> List(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) && v is List<object> l ? l : new List<object>();

        static string S(Dictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? v as string : null;

        static bool B(Dictionary<string, object> d, string key, bool fallback = false) =>
            d.TryGetValue(key, out var v) && v is bool b ? b : fallback;

        static double F(Dictionary<string, object> d, string key, double fallback) =>
            d.TryGetValue(key, out var v) && v is double x ? x : fallback;

        static T E<T>(Dictionary<string, object> d, string key) where T : struct {
            var s = S(d, key);
            if (s == null) return default;
            if (Enum.TryParse<T>(s, true, out var value)) return value;
            throw new FormatException($"game data: bad {typeof(T).Name} '{s}' for '{key}'");
        }

        static string[] Strings(Dictionary<string, object> d, string key) {
            var list = List(d, key);
            var result = new string[list.Count];
            for (var i = 0; i < list.Count; i++) result[i] = list[i] as string;
            return result;
        }

        static double[] Floats(Dictionary<string, object> d, string key) {
            var list = List(d, key);
            var result = new double[list.Count];
            for (var i = 0; i < list.Count; i++) result[i] = list[i] is double x ? x : 0;
            return result;
        }

        static int[] Ints(Dictionary<string, object> d, string key) {
            var list = List(d, key);
            var result = new int[list.Count];
            for (var i = 0; i < list.Count; i++) result[i] = list[i] is double x ? (int)Math.Round(x, MidpointRounding.AwayFromZero) : 0;
            return result;
        }

        internal static string Inv(double f) => f.ToString("R", CultureInfo.InvariantCulture);
    }
}
