using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoChess.Core;
using AutoChess.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AutoChess.Editor {
    /// <summary>
    /// Builds heroes from Tools/heroes/new_heroes.json: glb import settings, model prefab variant (shown meshes, body
    /// parts, animator override, mecanim), the HeroTrait asset (added to HeroTraitDB). Stats come from Tools/balance.
    /// Also renders check sheets of the models (Temp/hero_sheets).
    /// </summary>
    public static class HeroSetupTool {
        const string ConfigPath = "Tools/heroes/new_heroes.json";
        const string HeroModels = "Assets/_main/Models/Hero";
        const string BaseController = "Assets/_main/Models/Hero/_Base/_Base.controller";
        const string HeroTraits = "Assets/_main/Data/HeroTraits";
        const string HeroTraitDbPath = "Assets/_main/Data/_DB/Resources/HeroTraitDB.asset";
        const string SheetDir = "Temp/hero_sheets";

        [Serializable] class Config { public HeroConfig[] heroes; }

        [Serializable] class ClipConfig { public string slot; public string clip; }

        [Serializable] class PartConfig { public string name; public string[] materials; public bool enableAtStart; }

        [Serializable] class ParamConfig { public string key; public float value; public bool isPercentage; }

        [Serializable]
        class HeroConfig {
            public string id, folder, thumbnail, skillIcon;
            public string[] show;
            public PartConfig[] parts;
            public ClipConfig[] clips;
            public string name, subName, realm, role, reputation;
            public int attackRange;
            public float movementSpeed;
            public string skillName;
            public bool unstoppable;
            public ParamConfig[] skillParams;
            public string passiveDescription, activeDescription;
            public string[] specialKeys;
        }

        static Config Load() => JsonUtility.FromJson<Config>(File.ReadAllText(ConfigPath));

        [MenuItem("AutoChess/Hero Models/Set Up New Heroes")]
        public static void SetUpAll() {
            var config = Load();
            targetHeight = ReferenceHeight(config);
            foreach (var hero in config.heroes) SetUp(hero);
            AssetDatabase.SaveAssets();
            Debug.Log($"heroes set up: {string.Join(", ", config.heroes.Select(h => h.id))}");
        }

        [MenuItem("AutoChess/Hero Models/Render Check Sheets")]
        public static void RenderAll() {
            foreach (var hero in Load().heroes) RenderSheets(hero);
            Debug.Log($"hero sheets: {SheetDir}");
        }

        // ------------------------------------------------------------------ model

        static string GlbPath(HeroConfig hero) =>
            Directory.GetFiles($"{HeroModels}/{hero.folder}", "*.glb").Select(p => p.Replace('\\', '/')).First();

        static string PrefabPath(HeroConfig hero) => Path.ChangeExtension(GlbPath(hero), ".prefab").Replace('\\', '/');

        /// <summary>Model height (bind pose, shown meshes) the new heroes are scaled to: the median of the set-up heroes.</summary>
        static float targetHeight;

        static float ReferenceHeight(Config config) {
            var heights = new List<float>();
            var ours = config.heroes.Select(h => h.folder).ToHashSet();
            foreach (var dir in Directory.GetDirectories(HeroModels)) {
                var folder = Path.GetFileName(dir);
                if (folder.StartsWith("_") || ours.Contains(folder)) continue;
                var prefabPath = Directory.GetFiles(dir, "*.prefab").FirstOrDefault();
                if (prefabPath == null) continue;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath.Replace('\\', '/')));
                heights.Add(VisibleHeight(instance));
                if (instance != null) Object.DestroyImmediate(instance);
            }
            heights.Sort();
            var median = heights.Count == 0 ? 1.5f : heights[heights.Count / 2];
            Debug.Log($"hero reference height {median:0.###} (from {heights.Count} heroes: {string.Join(", ", heights.Select(h => h.ToString("0.##")))})");
            return median;
        }

        [MenuItem("AutoChess/Hero Models/Measure Heights")]
        public static void MeasureHeights() {
            var sb = new System.Text.StringBuilder();
            foreach (var dir in Directory.GetDirectories(HeroModels)) {
                var prefabPath = Directory.GetFiles(dir, "*.prefab").FirstOrDefault();
                if (prefabPath == null) continue;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath.Replace('\\', '/')));
                float aMin = float.MaxValue, aMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
                var cBounds = new Bounds();
                var first = true;
                var baked = new Mesh();
                foreach (var r in instance.GetComponentsInChildren<Renderer>()) {
                    if (first) cBounds = r.bounds;
                    else cBounds.Encapsulate(r.bounds);
                    first = false;
                    if (r is not SkinnedMeshRenderer s) continue;
                    s.BakeMesh(baked, false);
                    var a = r.transform.localToWorldMatrix;
                    var b = Matrix4x4.TRS(r.transform.position, r.transform.rotation, r.transform.lossyScale);
                    foreach (var v in baked.vertices) {
                        var ya = a.MultiplyPoint3x4(v).y;
                        var yb = b.MultiplyPoint3x4(v).y;
                        aMin = Mathf.Min(aMin, ya); aMax = Mathf.Max(aMax, ya);
                        bMin = Mathf.Min(bMin, yb); bMax = Mathf.Max(bMax, yb);
                    }
                }
                Object.DestroyImmediate(baked);
                sb.AppendLine($"{Path.GetFileName(dir)}: A {aMax - aMin:0.###} B {bMax - bMin:0.###} bounds {cBounds.size.y:0.###} root scale {instance.transform.localScale.x:0.###} child scale {instance.transform.GetChild(0).lossyScale.x:0.####}");
                Object.DestroyImmediate(instance);
            }
            File.WriteAllText("Temp/hero_heights.txt", sb.ToString());
        }

        [MenuItem("AutoChess/Hero Models/Reset New Hero Import Scale")]
        public static void ResetScale() {
            foreach (var hero in Load().heroes) {
                var importer = AssetImporter.GetAtPath(GlbPath(hero));
                var so = new SerializedObject(importer);
                so.FindProperty("_scaleFactor").floatValue = 0.01f;
                so.ApplyModifiedPropertiesWithoutUndo();
                importer.SaveAndReimport();
            }
        }

        /// <summary>Height of the bounds of the active meshes (the importer's bind-pose boxes).</summary>
        static float VisibleHeight(GameObject root) {
            var any = false;
            var bounds = new Bounds();
            foreach (var r in root.GetComponentsInChildren<Renderer>()) {
                if (!any) bounds = r.bounds;
                else bounds.Encapsulate(r.bounds);
                any = true;
            }
            return any ? bounds.size.y : 0;
        }

        static void SetUp(HeroConfig hero, int attempt = 0) {
            var glb = GlbPath(hero);
            var importer = AssetImporter.GetAtPath(glb);
            var so = new SerializedObject(importer);
            var scale = so.FindProperty("_scaleFactor");
            var instancing = so.FindProperty("_enableGpuInstancing");
            if (scale.floatValue > 0.5f || !instancing.boolValue) {
                if (scale.floatValue > 0.5f) scale.floatValue = 0.01f;
                instancing.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                importer.SaveAndReimport();
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(glb);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try {
                var partObjects = new List<(PartConfig part, GameObject obj)>();
                foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) {
                    var mats = r.sharedMaterials.Where(m => m).Select(m => m.name).ToArray();
                    var part = hero.parts?.FirstOrDefault(p => p.materials.Any(mats.Contains));
                    if (part != null) {
                        partObjects.Add((part, r.gameObject));
                        r.gameObject.SetActive(part.enableAtStart);
                        continue;
                    }
                    r.gameObject.SetActive(mats.Any(hero.show.Contains));
                }

                // chibi exports differ in scale: match the set-up heroes' height
                if (targetHeight > 0 && attempt == 0) {
                    // height at the default 0.01 import scale, so a re-run lands on the same scale
                    var height = VisibleHeight(instance) * 0.01f / scale.floatValue;
                    var wanted = 0.01f * Mathf.Sqrt(targetHeight / Mathf.Max(height, 1e-3f));
                    if (height > 0 && Mathf.Abs(wanted / scale.floatValue - 1) > 0.04f) {
                        Debug.Log($"{hero.id}: height {height:0.###}, reference {targetHeight:0.###}, import scale {wanted:0.#####}");
                        scale.floatValue = wanted;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        Object.DestroyImmediate(instance);
                        instance = null;
                        importer.SaveAndReimport();
                        SetUp(hero, attempt + 1);
                        return;
                    }
                }

                var animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = CreateOverride(hero, glb);

                var bodyParts = instance.GetComponent<BodyParts>() ?? instance.AddComponent<BodyParts>();
                var bp = new SerializedObject(bodyParts);
                var parts = bp.FindProperty("parts");
                parts.arraySize = partObjects.Count;
                for (var i = 0; i < partObjects.Count; i++) {
                    var e = parts.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("enableAtStart").boolValue = partObjects[i].part.enableAtStart;
                    e.FindPropertyRelative("name").stringValue = partObjects[i].part.name;
                    e.FindPropertyRelative("obj").objectReferenceValue = partObjects[i].obj;
                }
                bp.FindProperty("bones").arraySize = 0;
                bp.ApplyModifiedPropertiesWithoutUndo();

                var type = typeof(Mecanim).Assembly.GetType($"AutoChess.Game.Mecanim_{hero.id}")
                           ?? throw new InvalidOperationException($"no Mecanim_{hero.id} class");
                var mecanim = instance.GetComponent(type) ?? instance.AddComponent(type);
                var clips = Clips(glb);
                float Length(string slot) {
                    var name = hero.clips.FirstOrDefault(c => c.slot == slot)?.clip;
                    return name != null && clips.TryGetValue(name, out var c) ? c.length : 1;
                }
                var ms = new SerializedObject(mecanim);
                ms.FindProperty("rendererRoot").objectReferenceValue = instance.transform.GetChild(0).gameObject;
                ms.FindProperty("defaultAttackFullTime").floatValue = Length("Attack_0");
                var attackTime = ms.FindProperty("defaultAttackTime");
                attackTime.arraySize = 1;
                attackTime.GetArrayElementAtIndex(0).floatValue = 0.35f;
                var skillTimes = ms.FindProperty("skillFullTimes");
                skillTimes.arraySize = 1;
                skillTimes.GetArrayElementAtIndex(0).floatValue = Length("Skill_0");
                ms.FindProperty("diveInTime").floatValue = Length("Dive_In");
                ms.FindProperty("diveOutTimeRange").vector2Value = new Vector2(0.15f, 0.62f);
                ms.FindProperty("diveOutTime").floatValue = Length("Dive_Out");
                ms.ApplyModifiedPropertiesWithoutUndo();

                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath(hero));
                CreateTrait(hero, (Mecanim)prefab.GetComponent(type));
            }
            finally {
                if (instance != null) Object.DestroyImmediate(instance);
            }
        }

        static Dictionary<string, AnimationClip> Clips(string glb) {
            var map = new Dictionary<string, AnimationClip>();
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(glb).OfType<AnimationClip>()) {
                if (!clip.name.StartsWith("__preview")) map[clip.name] = clip;
            }
            return map;
        }

        static AnimatorOverrideController CreateOverride(HeroConfig hero, string glb) {
            var path = $"{HeroModels}/{hero.folder}/{hero.id}.overrideController";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            if (controller == null) {
                controller = new AnimatorOverrideController(AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(BaseController));
                AssetDatabase.CreateAsset(controller, path);
            }
            var clips = Clips(glb);
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            controller.GetOverrides(overrides);
            for (var i = 0; i < overrides.Count; i++) {
                var slot = hero.clips.FirstOrDefault(c => c.slot == overrides[i].Key.name);
                AnimationClip clip = null;
                if (slot != null && !clips.TryGetValue(slot.clip, out clip)) Debug.LogError($"{hero.id}: no clip '{slot.clip}'");
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, clip);
            }
            controller.ApplyOverrides(overrides);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        // ------------------------------------------------------------------ data

        static void CreateTrait(HeroConfig hero, Mecanim mecanim) {
            var path = $"{HeroTraits}/{hero.id}.asset";
            var trait = AssetDatabase.LoadAssetAtPath<HeroTrait>(path);
            var created = trait == null;
            if (created) trait = ScriptableObject.CreateInstance<HeroTrait>();

            trait.id = hero.id;
            trait.name = hero.name;
            trait.subName = hero.subName;
            trait.summoned = false;
            trait.realm = (Realm)Enum.Parse(typeof(Realm), hero.realm);
            trait.role = (Role)Enum.Parse(typeof(Role), hero.role);
            trait.reputation = (Reputation)Enum.Parse(typeof(Reputation), hero.reputation);
            trait.moveable = true;
            trait.mecanim = mecanim;
            trait.thumbnail = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/_main/Textures/2D/Heroes/{hero.thumbnail}.png");
            trait.skillIcon = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/_main/Textures/2D/Skills/{hero.skillIcon}.png");
            trait.attackRange = hero.attackRange;
            trait.movementSpeed = hero.movementSpeed;
            if (created) {
                // placeholders until balance.py apply writes the real numbers
                var magical = trait.role.HasFlag(Role.Sorcerer) || hero.id == Core.Combat.HeroIds.Gwen;
                trait.maxHp = 1000;
                trait.physicalDamage = magical ? 0 : 60;
                trait.magicalDamage = magical ? 60 : 0;
                trait.armor = 30;
                trait.resistance = 30;
                trait.attackSpeed = 0.75f;
                trait.energyRegenPerAttack = 10;
                trait.energyRegenPerHit = 8;
                var stat = magical ? Core.StatKeys.MagicalDamage : Core.StatKeys.PhysicalDamage;
                trait.evolutions = new[] {
                    Evolution(HeroRank.A, stat, 0.8f, 0.2f),
                    Evolution(HeroRank.S, stat, 2.24f, 0.5f),
                };
            }
            trait.skillName = hero.skillName;
            trait.unstoppable = hero.unstoppable;
            trait.skillParams = hero.skillParams.Select(p => new HeroSkillParam { key = p.key, value = p.value, isPercentage = p.isPercentage }).ToArray();
            trait.passiveDescription = hero.passiveDescription;
            trait.activeDescription = hero.activeDescription;
            trait.specialKeys = hero.specialKeys ?? Array.Empty<string>();

            if (created) AssetDatabase.CreateAsset(trait, path);
            EditorUtility.SetDirty(trait);

            var db = AssetDatabase.LoadAssetAtPath<HeroTraitDB>(HeroTraitDbPath);
            var dbo = new SerializedObject(db);
            var list = dbo.FindProperty("heroTraits");
            for (var i = 0; i < list.arraySize; i++) {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == trait) return;
            }
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = trait;
            dbo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(db);
        }

        static Evolution Evolution(HeroRank rank, string damageKey, float hpDmg, float atkSpd) => new() {
            rank = rank,
            modifiers = new[] {
                new AttributeModifier { key = Core.StatKeys.MaxHp, value = hpDmg, type = AttributeModifier.Type.Percentage },
                new AttributeModifier { key = damageKey, value = hpDmg, type = AttributeModifier.Type.Percentage },
                new AttributeModifier { key = Core.StatKeys.AttackSpeed, value = atkSpd, type = AttributeModifier.Type.Percentage },
            },
        };

        // ------------------------------------------------------------------ check sheets

        const int Cell = 320;

        /// <summary>
        /// Temp/hero_sheets/&lt;id&gt;_poses.png: the set-up model at a few frames of idle, attack and skill (rows);
        /// &lt;id&gt;_meshes.png: every mesh of the glb alone (index order, see &lt;id&gt;_meshes.txt).
        /// </summary>
        static void RenderSheets(HeroConfig hero) {
            Directory.CreateDirectory(SheetDir);
            var scene = EditorSceneManager.NewPreviewScene();
            try {
                var camera = new GameObject("cam", typeof(Camera)).GetComponent<Camera>();
                SceneManager_Move(camera.gameObject, scene);
                camera.scene = scene;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.32f, 0.36f, 0.42f, 1);
                camera.fieldOfView = 30;
                var light = new GameObject("light", typeof(Light)).GetComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(40, 150, 0);
                SceneManager_Move(light.gameObject, scene);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(hero));
                var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var clips = Clips(GlbPath(hero));
                var rows = new List<(AnimationClip clip, int frames)>();
                foreach (var slot in new[] { "Idle", "Attack_0", "Skill_0" }) {
                    var name = hero.clips.FirstOrDefault(c => c.slot == slot)?.clip;
                    if (name != null && clips.TryGetValue(name, out var clip)) rows.Add((clip, 5));
                }

                var poses = new Texture2D(Cell * 5, Cell * rows.Count, TextureFormat.RGB24, false);
                for (var r = 0; r < rows.Count; r++) {
                    for (var f = 0; f < rows[r].frames; f++) {
                        rows[r].clip.SampleAnimation(model, rows[r].clip.length * f / (rows[r].frames - 1));
                        var pixels = Render(camera, model, f == 0 ? 0 : 25);
                        poses.SetPixels(f * Cell, (rows.Count - 1 - r) * Cell, Cell, Cell, pixels);
                    }
                }
                File.WriteAllBytes($"{SheetDir}/{hero.id}_poses.png", poses.EncodeToPNG());
                Object.DestroyImmediate(poses);
                Object.DestroyImmediate(model);

                // every mesh alone, from the raw glb
                var raw = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath(hero)), scene);
                var renderers = raw.GetComponentsInChildren<Renderer>(true);
                if (clips.TryGetValue(hero.clips.First(c => c.slot == "Idle").clip, out var idle)) idle.SampleAnimation(raw, 0);
                const int cols = 6;
                var small = Cell / 2;
                var meshRows = (renderers.Length + cols - 1) / cols;
                var sheet = new Texture2D(small * cols, small * meshRows, TextureFormat.RGB24, false);
                var lines = new List<string>();
                for (var i = 0; i < renderers.Length; i++) {
                    foreach (var other in renderers) other.gameObject.SetActive(other == renderers[i]);
                    var pixels = Render(camera, raw, 0, small, renderers[i]);
                    sheet.SetPixels(i % cols * small, (meshRows - 1 - i / cols) * small, small, small, pixels);
                    var mats = string.Join("+", renderers[i].sharedMaterials.Where(m => m).Select(m => m.name));
                    lines.Add($"{i}: {mats} ({(hero.show.Any(s => mats.Split('+').Contains(s)) ? "shown" : "hidden")})");
                }
                File.WriteAllBytes($"{SheetDir}/{hero.id}_meshes.png", sheet.EncodeToPNG());
                File.WriteAllLines($"{SheetDir}/{hero.id}_meshes.txt", lines);
                Object.DestroyImmediate(sheet);
            }
            finally {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [MenuItem("AutoChess/Hero Models/Render Clip Sheets")]
        public static void RenderAllClips() {
            foreach (var hero in Load().heroes) RenderClips(hero);
            Debug.Log($"hero clip sheets: {SheetDir}");
        }

        /// <summary>
        /// Temp/hero_sheets/&lt;id&gt;_clips_&lt;n&gt;.png: every clip of the glb on the set-up model, 6 frames per row, 8 rows
        /// per sheet (names in &lt;id&gt;_clips.txt).
        /// </summary>
        static void RenderClips(HeroConfig hero) {
            Directory.CreateDirectory(SheetDir);
            var scene = EditorSceneManager.NewPreviewScene();
            try {
                var camera = new GameObject("cam", typeof(Camera)).GetComponent<Camera>();
                SceneManager_Move(camera.gameObject, scene);
                camera.scene = scene;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.32f, 0.36f, 0.42f, 1);
                camera.fieldOfView = 30;
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(hero)), scene);
                var clips = Clips(GlbPath(hero)).Values.OrderBy(c => c.name).ToList();
                const int frames = 6, perSheet = 8, size = 160;
                var lines = new List<string>();
                for (var start = 0; start < clips.Count; start += perSheet) {
                    var count = Math.Min(perSheet, clips.Count - start);
                    var sheet = new Texture2D(size * frames, size * count, TextureFormat.RGB24, false);
                    for (var r = 0; r < count; r++) {
                        var clip = clips[start + r];
                        lines.Add($"sheet {start / perSheet} row {r}: {clip.name} ({clip.length:0.###}s)");
                        for (var f = 0; f < frames; f++) {
                            clip.SampleAnimation(model, clip.length * f / (frames - 1));
                            var pixels = Render(camera, model, 0, size);
                            sheet.SetPixels(f * size, (count - 1 - r) * size, size, size, pixels);
                        }
                    }
                    File.WriteAllBytes($"{SheetDir}/{hero.id}_clips_{start / perSheet}.png", sheet.EncodeToPNG());
                    Object.DestroyImmediate(sheet);
                }
                File.WriteAllLines($"{SheetDir}/{hero.id}_clips.txt", lines);
            }
            finally {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene) =>
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);

        /// <summary>Frames the visible renderers (or <paramref name="only"/>) from the front, turned by <paramref name="yaw"/>.</summary>
        static Color[] Render(Camera camera, GameObject root, float yaw, int size = Cell, Renderer only = null) {
            var bounds = new Bounds();
            var any = false;
            foreach (var r in only != null ? new[] { only } : root.GetComponentsInChildren<Renderer>()) {
                if (!r.gameObject.activeInHierarchy) continue;
                if (!any) bounds = r.bounds;
                else bounds.Encapsulate(r.bounds);
                any = true;
            }
            if (!any) bounds = new Bounds(Vector3.up, Vector3.one);
            var radius = Mathf.Max(bounds.extents.magnitude, 0.05f);
            var dir = Quaternion.Euler(10, 180 + yaw, 0) * Vector3.forward;
            camera.transform.position = bounds.center - dir * (radius * 0.75f / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
            camera.transform.LookAt(bounds.center);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = radius * 10 + 10;

            var rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            RenderTexture.active = previous;
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            var pixels = tex.GetPixels();
            Object.DestroyImmediate(tex);
            return pixels;
        }
    }
}
