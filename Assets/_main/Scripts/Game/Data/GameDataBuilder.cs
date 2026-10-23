using System;
using System.Collections.Generic;
using AutoChess.Core;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>
    /// Turns the ScriptableObject authoring data into the engine-free definitions of AutoChess.Core. The
    /// client builds its <see cref="GameDatabase"/> with it; the editor exports the same content as JSON
    /// (GameData/game_data.json) for a dedicated server and the offline tests.
    /// </summary>
    public static class GameDataBuilder {
        public static GameDataJson.Content BuildContent(IEnumerable<Stage> stages, double cellWidth, double cellHeight) {
            var content = new GameDataJson.Content();
            content.Rules.CellWidth = cellWidth;
            content.Rules.CellHeight = cellHeight;

            foreach (var trait in HeroTraitDB.Instance.All) {
                if (trait != null) content.Units.Add(ToUnit(trait));
            }
            var itemDb = ItemDB.Instance;
            foreach (var item in itemDb.RawItems) {
                if (item != null) content.Items.Add(ToItem(item));
            }
            foreach (var item in itemDb.ForgedItems) {
                if (item != null) content.Items.Add(ToItem(item));
            }
            foreach (var config in DestinyDB.Instance.All) {
                if (config != null) content.Traits.Add(ToTrait(config));
            }
            if (stages != null) {
                foreach (var stage in stages) {
                    if (stage != null) content.Stages.Add(ToStage(stage));
                }
            }
            return content;
        }

        public static GameDatabase Build(IEnumerable<Stage> stages, double cellWidth, double cellHeight) =>
            BuildContent(stages, cellWidth, cellHeight).ToDatabase();

        public static UnitDefinition ToUnit(HeroTrait t) {
            var evolutions = new StatModifier[3][];
            for (var i = 0; i < evolutions.Length; i++) evolutions[i] = Array.Empty<StatModifier>();
            if (t.evolutions != null) {
                foreach (var e in t.evolutions) {
                    if (e?.modifiers != null) evolutions[(int)e.rank] = Convert(e.modifiers);
                }
            }

            var paramCount = t.skillParams?.Length ?? 0;
            var values = new double[paramCount];
            var keys = new string[paramCount];
            for (var i = 0; i < paramCount; i++) {
                values[i] = t.skillParams[i].value;
                keys[i] = t.skillParams[i].key;
            }

            return new UnitDefinition {
                Id = t.id, Name = t.name, Summoned = t.summoned, Realm = t.realm, Roles = t.role,
                Reputation = t.reputation, Moveable = t.moveable,
                MaxHp = t.maxHp, PhysicalDamage = t.physicalDamage, MagicalDamage = t.magicalDamage, Armor = t.armor,
                Resistance = t.resistance, AttackSpeed = t.attackSpeed, MovementSpeed = t.movementSpeed,
                EnergyRegenPerAttack = t.energyRegenPerAttack, EnergyRegenPerHit = t.energyRegenPerHit,
                StartingEnergy = t.startingEnergy,
                AttackRange = t.attackRange, Unstoppable = t.unstoppable, Evolutions = evolutions,
                SkillParams = values, SkillParamKeys = keys, SpecialKeys = t.specialKeys ?? Array.Empty<string>(),
            };
        }

        public static ItemDefinition ToItem(Item item) {
            var ingredients = item.IsForgedItem() ? new[] { item.ingredients[0].Id, item.ingredients[1].Id } : Array.Empty<string>();
            var values = new double[item.itemParams?.Length ?? 0];
            for (var i = 0; i < values.Length; i++) values[i] = item.itemParams[i].value;
            return new ItemDefinition { Id = item.Id, Name = item.name, Ingredients = ingredients, Modifiers = Convert(item.modifiers), Params = values };
        }

        public static TraitDefinition ToTrait(DestinyConfig config) {
            var values = new double[config.destinyParams?.Length ?? 0];
            for (var i = 0; i < values.Length; i++) values[i] = config.destinyParams[i].value;
            var def = new TraitDefinition {
                Name = config.GetName(), Checkpoints = config.checkpoints ?? Array.Empty<int>(), Params = values,
            };
            switch (config) {
                case RoleConfig role:
                    def.Kind = TraitKind.Role;
                    def.Role = role.role;
                    break;
                case RealmConfig realm:
                    def.Kind = TraitKind.Realm;
                    def.Realm = realm.realm;
                    break;
            }
            return def;
        }

        public static StageDefinition ToStage(Stage stage) {
            var rounds = new RoundDefinition[stage.matches?.Length ?? 0];
            for (var r = 0; r < rounds.Length; r++) {
                var m = stage.matches[r];
                var enemies = new EnemyDefinition[m.enemies?.Length ?? 0];
                for (var i = 0; i < enemies.Length; i++) {
                    var e = m.enemies[i];
                    var items = new List<string>();
                    if (e.items != null) {
                        foreach (var it in e.items) {
                            if (it != null) items.Add(it.Id);
                        }
                    }
                    enemies[i] = new EnemyDefinition {
                        UnitId = e.heroId, Rank = e.rank, Cell = new HexCoord(e.gridPosition.x, e.gridPosition.y),
                        Items = items.ToArray(), HpScale = e.hpScale, DamageScale = e.damageScale,
                    };
                }
                rounds[r] = new RoundDefinition { Enemies = enemies, Rewards = Convert(m.rewards), WinRewards = Convert(m.winRewards) };
            }
            return new StageDefinition { Name = stage.name, Rounds = rounds };
        }

        static StatModifier[] Convert(AttributeModifier[] modifiers) {
            if (modifiers == null) return Array.Empty<StatModifier>();
            var result = new StatModifier[modifiers.Length];
            for (var i = 0; i < modifiers.Length; i++) result[i] = modifiers[i].ToCore();
            return result;
        }

        static RewardDefinition[] Convert(Reward[] rewards) {
            if (rewards == null) return Array.Empty<RewardDefinition>();
            var result = new RewardDefinition[rewards.Length];
            for (var i = 0; i < rewards.Length; i++) result[i] = Convert(rewards[i]);
            return result;
        }

        static RewardDefinition Convert(Reward reward) {
            var def = new RewardDefinition {
                Coins = reward.coins, MinCoins = reward.minCoins, MaxCoins = reward.maxCoins, Reputation = reward.reputation,
            };
            switch (reward.type) {
                case RewardType.Coin: def.Kind = RewardKind.Coin; break;
                case RewardType.RandomCoin: def.Kind = RewardKind.RandomCoin; break;
                case RewardType.Hero: def.Kind = RewardKind.Unit; break;
                case RewardType.RawItem: def.Kind = RewardKind.RawItem; break;
                case RewardType.ForgedItem: def.Kind = RewardKind.ForgedItem; break;
                case RewardType.RandomReward:
                    def.Kind = RewardKind.Random;
                    var options = new List<WeightedReward>();
                    if (reward.randomRewards != null) {
                        foreach (var o in reward.randomRewards) {
                            if (o?.reward != null) options.Add(new WeightedReward { Reward = Convert(o.reward), Rate = o.rate });
                        }
                    }
                    def.Options = options.ToArray();
                    break;
                default:
                    Debug.LogWarning($"Unknown reward type '{reward.type}', treated as coins");
                    def.Kind = RewardKind.Coin;
                    break;
            }
            return def;
        }
    }
}
