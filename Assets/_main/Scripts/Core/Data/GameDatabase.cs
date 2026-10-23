using System;
using System.Collections.Generic;

namespace AutoChess.Core {
    /// <summary>
    /// Validated, indexed view of every definition the simulation uses. Built once and shared (read-only)
    /// by all matches, so one instance can serve many matches on a server.
    /// </summary>
    public sealed class GameDatabase {
        public readonly GameRules Rules;
        public readonly HexGrid Grid;
        public readonly IReadOnlyList<StageDefinition> Stages;

        readonly Dictionary<string, UnitDefinition> units = new(StringComparer.Ordinal);
        readonly Dictionary<string, ItemDefinition> items = new(StringComparer.Ordinal);
        readonly Dictionary<TraitId, TraitDefinition> traits = new();
        readonly Dictionary<(string, string), ItemDefinition> recipes = new();
        readonly List<UnitDefinition>[] shopPool;
        readonly List<UnitDefinition> unitList = new();
        readonly List<ItemDefinition> components = new();
        readonly List<ItemDefinition> combined = new();

        public IReadOnlyList<UnitDefinition> Units => unitList;
        public IReadOnlyList<ItemDefinition> Components => components;
        public IReadOnlyList<ItemDefinition> CombinedItems => combined;
        public IEnumerable<TraitDefinition> Traits => traits.Values;

        public GameDatabase(GameRules rules, IEnumerable<UnitDefinition> unitDefs, IEnumerable<ItemDefinition> itemDefs,
            IEnumerable<TraitDefinition> traitDefs, IEnumerable<StageDefinition> stages) {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Grid = new HexGrid(rules.BoardRows, rules.BoardColumns, rules.CellWidth, rules.CellHeight);

            foreach (var u in unitDefs) {
                if (string.IsNullOrEmpty(u?.Id)) throw new ArgumentException("unit without id");
                if (!units.TryAdd(u.Id, u)) throw new ArgumentException($"duplicate unit id '{u.Id}'");
                unitList.Add(u);
            }

            foreach (var i in itemDefs) {
                if (string.IsNullOrEmpty(i?.Id)) throw new ArgumentException("item without id");
                if (!items.TryAdd(i.Id, i)) throw new ArgumentException($"duplicate item id '{i.Id}'");
            }
            foreach (var i in items.Values) {
                if (i.IsCombined) {
                    foreach (var ing in i.Ingredients) {
                        if (!items.ContainsKey(ing)) throw new ArgumentException($"item '{i.Id}': unknown ingredient '{ing}'");
                    }
                    recipes[Key(i.Ingredients[0], i.Ingredients[1])] = i;
                    combined.Add(i);
                }
                else {
                    components.Add(i);
                }
            }

            foreach (var t in traitDefs) {
                traits[t.Id] = t;
            }

            var reputations = Enum.GetValues(typeof(Reputation)).Length;
            shopPool = new List<UnitDefinition>[reputations];
            for (var r = 0; r < reputations; r++) shopPool[r] = new List<UnitDefinition>();
            foreach (var u in unitList) {
                if (!u.Summoned && u.Reputation != Reputation.None) shopPool[(int)u.Reputation].Add(u);
            }

            var stageList = new List<StageDefinition>(stages ?? Array.Empty<StageDefinition>());
            foreach (var s in stageList) {
                foreach (var round in s.Rounds) {
                    foreach (var e in round.Enemies) {
                        if (!units.ContainsKey(e.UnitId)) throw new ArgumentException($"stage '{s.Name}': unknown enemy '{e.UnitId}'");
                        if (!Grid.Contains(e.Cell)) throw new ArgumentException($"stage '{s.Name}': enemy '{e.UnitId}' outside the board at {e.Cell}");
                        foreach (var it in e.Items) {
                            if (!items.ContainsKey(it)) throw new ArgumentException($"stage '{s.Name}': unknown item '{it}'");
                        }
                    }
                }
            }
            Stages = stageList;
        }

        static (string, string) Key(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

        public UnitDefinition Unit(string id) {
            if (id != null && units.TryGetValue(id, out var u)) return u;
            throw new KeyNotFoundException($"unknown unit '{id}'");
        }

        public bool TryGetUnit(string id, out UnitDefinition unit) => units.TryGetValue(id ?? string.Empty, out unit);

        public ItemDefinition Item(string id) {
            if (id != null && items.TryGetValue(id, out var i)) return i;
            throw new KeyNotFoundException($"unknown item '{id}'");
        }

        public bool TryGetItem(string id, out ItemDefinition item) => items.TryGetValue(id ?? string.Empty, out item);

        /// <summary>The combined item made of two components, or null.</summary>
        public ItemDefinition Combine(string a, string b) => recipes.TryGetValue(Key(a, b), out var i) ? i : null;

        public TraitDefinition Trait(TraitId id) => traits.TryGetValue(id, out var t) ? t : null;

        /// <summary>Units the shop can roll for a reputation (summoned units excluded).</summary>
        public IReadOnlyList<UnitDefinition> ShopPool(Reputation reputation) => shopPool[(int)reputation];
    }
}
