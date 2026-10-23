using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoChess.Core;
using AutoChess.Game;
using UnityEditor;
using UnityEngine;

namespace AutoChess.Editor {
    /// <summary>
    /// Writes Tools/items/items.json into the Item assets (Data/Items): component stats, combined items' stats (both
    /// components + extra), passive params and descriptions. Names, icons and recipes stay as authored.
    /// </summary>
    public static class ItemConfigTool {
        const string ConfigPath = "Tools/items/items.json";
        const string ItemDir = "Assets/_main/Data/Items";

        [MenuItem("AutoChess/Items/Apply Item Config")]
        public static void Apply() {
            var root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(ConfigPath));
            var components = new Dictionary<string, List<AttributeModifier>>();
            foreach (var pair in (Dictionary<string, object>)root["components"]) {
                components[pair.Key] = Modifiers((List<object>)pair.Value).ToList();
                var item = Load(pair.Key);
                item.modifiers = components[pair.Key].ToArray();
                item.itemParams = Array.Empty<HeroSkillParam>();
                item.description = string.Empty;
                EditorUtility.SetDirty(item);
            }

            var done = new List<string>();
            foreach (Dictionary<string, object> entry in (List<object>)root["items"]) {
                var id = (string)entry["id"];
                var item = Load(id);
                var mods = new List<AttributeModifier>();
                foreach (var ingredient in item.ingredients) Merge(mods, components[ingredient.Id]);
                Merge(mods, Modifiers((List<object>)entry["extra"]));
                item.modifiers = mods.ToArray();
                item.itemParams = ((List<object>)entry["params"]).Select(p => {
                    var list = (List<object>)p;
                    return new HeroSkillParam { key = (string)list[0], value = Convert.ToSingle(list[1]), isPercentage = (bool)list[2] };
                }).ToArray();
                item.description = (string)entry["description"];
                EditorUtility.SetDirty(item);
                done.Add(id);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"item config applied: {components.Count} components, {done.Count} combined items");
        }

        static Item Load(string id) =>
            AssetDatabase.LoadAssetAtPath<Item>($"{ItemDir}/{id}.asset") ?? throw new InvalidOperationException($"no item asset {id}");

        static IEnumerable<AttributeModifier> Modifiers(List<object> list) {
            foreach (Dictionary<string, object> m in list) {
                var stat = (StatKind)Enum.Parse(typeof(StatKind), (string)m["stat"]);
                yield return new AttributeModifier {
                    key = StatKeys.KeyOf(stat), value = Convert.ToSingle(m["value"]),
                    type = (string)m["type"] == "Percent" ? AttributeModifier.Type.Percentage : AttributeModifier.Type.FixedValue,
                };
            }
        }

        /// <summary>Adds modifiers, summing the ones of the same stat and type.</summary>
        static void Merge(List<AttributeModifier> into, IEnumerable<AttributeModifier> add) {
            foreach (var m in add) {
                var same = into.FirstOrDefault(x => x.key == m.key && x.type == m.type);
                if (same != null) same.value += m.value;
                else into.Add(new AttributeModifier { key = m.key, value = m.value, type = m.type });
            }
        }
    }
}
