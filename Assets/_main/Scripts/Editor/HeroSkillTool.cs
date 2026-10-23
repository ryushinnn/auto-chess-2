using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoChess.Core;
using AutoChess.Core.Combat;
using AutoChess.Game;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AutoChess.Editor {
    /// <summary>
    /// Writes Tools/heroes/hero_skills.json into the HeroTrait assets (only the fields an entry lists), and builds
    /// Zed's shadow clone (Zed's model on the AutoChess/Shadow Clone shader + a summoned HeroTrait).
    /// </summary>
    public static class HeroSkillTool {
        const string ConfigPath = "Tools/heroes/hero_skills.json";
        const string HeroTraits = "Assets/_main/Data/HeroTraits";
        const string HeroTraitDbPath = "Assets/_main/Data/_DB/Resources/HeroTraitDB.asset";
        const string ShadowMaterial = "Assets/_main/Materials/ShadowClone.mat";

        [MenuItem("AutoChess/Heroes/Apply Hero Skills")]
        public static void Apply() {
            var root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(ConfigPath));
            var done = new List<string>();
            foreach (Dictionary<string, object> entry in (List<object>)root["heroes"]) {
                var id = (string)entry["id"];
                var trait = AssetDatabase.LoadAssetAtPath<HeroTrait>($"{HeroTraits}/{id}.asset")
                            ?? throw new InvalidOperationException($"no HeroTrait {id}");
                if (entry.TryGetValue("role", out var role)) trait.role = (Role)Enum.Parse(typeof(Role), (string)role);
                if (entry.TryGetValue("unstoppable", out var unstoppable)) trait.unstoppable = (bool)unstoppable;
                if (entry.TryGetValue("skillName", out var skillName)) trait.skillName = (string)skillName;
                if (entry.TryGetValue("params", out var list)) {
                    trait.skillParams = ((List<object>)list).Select(p => {
                        var item = (List<object>)p;
                        return new HeroSkillParam { key = (string)item[0], value = Convert.ToSingle(item[1]), isPercentage = (bool)item[2] };
                    }).ToArray();
                }
                if (entry.TryGetValue("passive", out var passive)) trait.passiveDescription = (string)passive;
                if (entry.TryGetValue("active", out var active)) trait.activeDescription = (string)active;
                if (entry.TryGetValue("specialKeys", out var keys)) trait.specialKeys = ((List<object>)keys).Cast<string>().ToArray();
                EditorUtility.SetDirty(trait);
                done.Add(id);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"hero skills applied: {string.Join(", ", done)}");
        }

        [MenuItem("AutoChess/Heroes/Build Zed Shadow")]
        public static void BuildZedShadow() {
            var material = AssetDatabase.LoadAssetAtPath<Material>(ShadowMaterial);
            if (material == null) {
                material = new Material(Shader.Find("AutoChess/Shadow Clone"));
                AssetDatabase.CreateAsset(material, ShadowMaterial);
            }

            var zed = AssetDatabase.LoadAssetAtPath<HeroTrait>($"{HeroTraits}/{HeroIds.Zed}.asset");
            var zedPrefabPath = AssetDatabase.GetAssetPath(zed.mecanim);
            var shadowPath = Path.Combine(Path.GetDirectoryName(zedPrefabPath) ?? "", "zed_shadow.prefab").Replace('\\', '/');
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(zedPrefabPath));
            GameObject prefab;
            try {
                foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) {
                    r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                prefab = PrefabUtility.SaveAsPrefabAsset(instance, shadowPath);
            }
            finally {
                Object.DestroyImmediate(instance);
            }

            var path = $"{HeroTraits}/{HeroIds.ZedShadow}.asset";
            var trait = AssetDatabase.LoadAssetAtPath<HeroTrait>(path);
            var created = trait == null;
            if (created) trait = ScriptableObject.CreateInstance<HeroTrait>();
            trait.id = HeroIds.ZedShadow;
            trait.name = "Phân Thân";
            trait.subName = "(Rét)";
            trait.summoned = true;
            trait.moveable = true;
            trait.mecanim = prefab.GetComponent<Mecanim>();
            trait.skillIcon = zed.skillIcon;
            // stats are replaced by a share of Zed's when a clone appears (ZedSkill); these only fill the inspector
            trait.maxHp = zed.maxHp;
            trait.physicalDamage = zed.physicalDamage;
            trait.magicalDamage = zed.magicalDamage;
            trait.armor = zed.armor;
            trait.resistance = zed.resistance;
            trait.attackSpeed = zed.attackSpeed;
            trait.movementSpeed = zed.movementSpeed;
            trait.attackRange = zed.attackRange;
            trait.energyRegenPerAttack = 0;
            trait.energyRegenPerHit = 0;
            trait.startingEnergy = 0;
            trait.evolutions = Array.Empty<Evolution>();
            trait.skillName = "";
            trait.skillParams = Array.Empty<HeroSkillParam>();
            trait.passiveDescription = "";
            trait.activeDescription = "Phân thân của Rét: chiến đấu như một tướng, không có kỹ năng.";
            trait.specialKeys = Array.Empty<string>();
            if (created) AssetDatabase.CreateAsset(trait, path);
            EditorUtility.SetDirty(trait);

            var db = AssetDatabase.LoadAssetAtPath<HeroTraitDB>(HeroTraitDbPath);
            var so = new SerializedObject(db);
            var listProperty = so.FindProperty("heroTraits");
            var present = false;
            for (var i = 0; i < listProperty.arraySize; i++) present |= listProperty.GetArrayElementAtIndex(i).objectReferenceValue == trait;
            if (!present) {
                listProperty.arraySize++;
                listProperty.GetArrayElementAtIndex(listProperty.arraySize - 1).objectReferenceValue = trait;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(db);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Zed shadow built: {shadowPath}, {path}");
        }
    }
}
