using System;
using System.Collections.Generic;
using System.IO;
using AutoChess.Core.Combat.Heroes;
using AutoChess.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AutoChess.Editor {
    /// <summary>
    /// Builds the skill VFX prefabs (Prefabs/VFX/&lt;key&gt;.prefab, key = the Core visual name) from code: particle systems
    /// on the AutoChess/VFX Particle shader with the textures of Tools/vfx/make_textures.py. Re-run after changing a
    /// recipe; the prefabs keep their GUIDs. Register new keys in the Arena's VfxPool.
    /// </summary>
    public static partial class VfxBuilder {
        const string TextureDir = "Assets/_main/Textures/VFX";
        const string MaterialDir = "Assets/_main/Materials/VFX";
        const string PrefabDir = "Assets/_main/Prefabs/VFX";
        const float Cell = 1.88f;
        const float Across = Cell * 3; // a radius of one cell

        [MenuItem("AutoChess/VFX/Build Skill VFX")]
        public static void BuildAll() {
            Directory.CreateDirectory(MaterialDir);
            SetUpTextures();
            Build(AhriSkill.OrbVisual, Ahri);
            Build(AmumuSkill.CurseVisual, Amumu);
            Build(AnnieSkill.SlamVisual, Annie);
            Build(BlitzcrankSkill.GrabVisual, BlitzGrab);
            Build(BlitzcrankSkill.ShieldVisual, BlitzShield);
            Build(GwenSkill.SnipVisual, Gwen);
            Build(JannaSkill.MonsoonVisual, Janna);
            Build(KaisaSkill.MissileVisual, Kaisa);
            Build(LeeSinSkill.KickVisual, LeeKick);
            Build(LeeSinSkill.LandVisual, LeeLand);
            Build(LuluSkill.GrowthVisual, Lulu);
            Build(LuxSkill.SparkVisual, Lux);
            BuildMore();
            AssetDatabase.SaveAssets();
            Debug.Log("skill VFX built");
        }

        public static readonly string[] Keys = {
            AhriSkill.OrbVisual, AmumuSkill.CurseVisual, AnnieSkill.SlamVisual, BlitzcrankSkill.GrabVisual,
            BlitzcrankSkill.ShieldVisual, GwenSkill.SnipVisual, JannaSkill.MonsoonVisual, KaisaSkill.MissileVisual,
            LeeSinSkill.KickVisual, LeeSinSkill.LandVisual, LuluSkill.GrowthVisual, LuxSkill.SparkVisual,
        };

        // ------------------------------------------------------------------ recipes

        static void Ahri(GameObject root, SkillVfx vfx) {
            Single(root, "Core", Mat("glow"), 0.55f, 1.1f, new Color(0.55f, 0.75f, 1f));
            Single(root, "Heart", Mat("spark"), 0.55f, 0.7f, new Color(1f, 0.6f, 0.9f));
            Trail(root, "Trail", Mat("glow"), 30, 0.45f, 0.45f, new Color(1f, 0.5f, 0.85f), new Color(0.4f, 0.7f, 1f));
            Set(vfx, "linger", 0.5f);
        }

        static void Amumu(GameObject root, SkillVfx vfx) {
            Ground(root, "Ring", Mat("ring"), 0.6f, Across, new Color(0.3f, 1f, 0.85f), grow: true);
            Ground(root, "Pool", Mat("glow"), 1.0f, Across * 0.9f, new Color(0.2f, 0.8f, 0.7f, 0.6f), grow: false);
            Burst(root, "Tears", Mat("spark"), 30, 0.7f, 0.35f, new Color(0.6f, 1f, 1f), speed: 6, radius: 0.4f, up: 0.6f);
            Set(vfx, "height", 0f);
        }

        static void Annie(GameObject root, SkillVfx vfx) {
            Burst(root, "Flames", Mat("flame"), 26, 0.8f, 1.3f, new Color(1f, 0.55f, 0.15f), speed: 1.5f, radius: Cell, up: 2.5f,
                endColor: new Color(1f, 0.15f, 0.05f));
            Ground(root, "Ring", Mat("ring"), 0.5f, Across, new Color(1f, 0.5f, 0.1f), grow: true);
            Burst(root, "Smoke", Mat("smoke", additive: false), 10, 1.2f, 1.4f, new Color(0.25f, 0.2f, 0.2f, 0.7f), speed: 0.8f,
                radius: Cell * 0.8f, up: 1.2f);
        }

        static void BlitzGrab(GameObject root, SkillVfx vfx) {
            Beam(root, vfx, Mat("beam", scroll: new Vector2(0, -4), color: new Color(1f, 0.85f, 0.25f)), 0.55f);
            Single(root, "Hand", Mat("glow"), 0.5f, 1.2f, new Color(1f, 0.9f, 0.4f));
            Set(vfx, "beamFadeOut", 0.12f);
        }

        static void BlitzShield(GameObject root, SkillVfx vfx) {
            var bubble = Emitter(root, "Bubble", Mat("ring"), loop: true);
            var main = bubble.main;
            main.startLifetime = 1.2f;
            main.startSize = 2.8f;
            main.startColor = new Color(1f, 1f, 1f, 0.8f);
            var emission = bubble.emission;
            emission.rateOverTime = 1.6f;
            Fade(bubble, Color.white, Color.white, 0.35f);
            var size = bubble.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0.85f, 1, 1.05f));
            bubble.transform.localPosition = Vector3.up * 1f;
            var glow = Emitter(root, "Glow", Mat("glow"), loop: true);
            main = glow.main;
            main.startLifetime = 1f;
            main.startSize = 3.2f;
            main.startColor = new Color(0.9f, 0.95f, 1f, 0.35f);
            emission = glow.emission;
            emission.rateOverTime = 2;
            Fade(glow, Color.white, Color.white, 0.3f);
            glow.transform.localPosition = Vector3.up * 1f;
            Set(vfx, "linger", 0.8f);
        }

        static void Gwen(GameObject root, SkillVfx vfx) {
            var slash = Burst(root, "Slash", Mat("slash"), 2, 0.28f, 2.4f, new Color(0.75f, 0.9f, 1f), speed: 0, radius: 0, up: 0);
            var main = slash.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            slash.transform.localPosition = Vector3.up * 1f;
            var sparks = Burst(root, "Sparks", Mat("spark"), 12, 0.35f, 0.3f, Color.white, speed: 5, radius: 0.2f, up: 0);
            sparks.transform.localPosition = Vector3.up * 1f;
            Set(vfx, "height", 0f);
            Set(vfx, "linger", 0.4f);
        }

        static void Janna(GameObject root, SkillVfx vfx) {
            var swirl = Emitter(root, "Swirl", Mat("swirl"), loop: true);
            var main = swirl.main;
            main.startLifetime = 1.4f;
            main.startSize = Across;
            main.startColor = new Color(0.6f, 0.9f, 1f, 0.7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var emission = swirl.emission;
            emission.rateOverTime = 3;
            var rotation = swirl.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-Mathf.PI * 1.5f);
            Fade(swirl, Color.white, Color.white, 0.3f);
            var renderer = swirl.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            swirl.transform.localPosition = Vector3.up * 0.1f;

            var motes = Emitter(root, "Motes", Mat("spark"), loop: true);
            main = motes.main;
            main.startLifetime = 1.1f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
            main.startSpeed = 0;
            main.startColor = new Color(0.75f, 1f, 0.85f);
            emission = motes.emission;
            emission.rateOverTime = 24;
            var shape = motes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Across * 0.45f;
            shape.rotation = new Vector3(90, 0, 0);
            var velocity = Rise(motes, 1.2f, 2.2f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(2f);
            Fade(motes, Color.white, Color.white, 0.25f);
            Set(vfx, "height", 0f);
            Set(vfx, "linger", 1f);
        }

        static void Kaisa(GameObject root, SkillVfx vfx) {
            Single(root, "Core", Mat("glow"), 0.28f, 0.6f, new Color(0.8f, 0.4f, 1f));
            Trail(root, "Trail", Mat("glow"), 35, 0.25f, 0.28f, new Color(1f, 0.4f, 0.9f), new Color(0.5f, 0.3f, 1f));
            Set(vfx, "height", 1.2f);
            Set(vfx, "linger", 0.3f);
        }

        static void LeeKick(GameObject root, SkillVfx vfx) {
            var star = Burst(root, "Impact", Mat("spark"), 1, 0.3f, 3f, new Color(1f, 0.8f, 0.3f), speed: 0, radius: 0, up: 0);
            star.transform.localPosition = Vector3.up * 1f;
            var flames = Burst(root, "Flames", Mat("flame"), 14, 0.5f, 1f, new Color(1f, 0.6f, 0.15f), speed: 4, radius: 0.3f, up: 0,
                endColor: new Color(1f, 0.2f, 0.05f));
            flames.transform.localPosition = Vector3.up * 1f;
            Ground(root, "Ring", Mat("ring"), 0.4f, Cell * 1.6f, new Color(1f, 0.75f, 0.25f), grow: true);
            Set(vfx, "height", 0f);
        }

        static void LeeLand(GameObject root, SkillVfx vfx) {
            Ground(root, "Shockwave", Mat("ring"), 0.6f, Across, new Color(1f, 0.85f, 0.5f), grow: true);
            Burst(root, "Dust", Mat("smoke", additive: false), 12, 1f, 1.2f, new Color(0.55f, 0.45f, 0.35f, 0.7f), speed: 2.5f,
                radius: 0.5f, up: 0.3f);
            Set(vfx, "height", 0f);
        }

        static void Lulu(GameObject root, SkillVfx vfx) {
            var pop = Burst(root, "Pop", Mat("spark"), 1, 0.45f, 3.4f, new Color(1f, 0.6f, 0.95f), speed: 0, radius: 0, up: 0);
            pop.transform.localPosition = Vector3.up * 1.2f;
            var sparkles = Emitter(root, "Sparkles", Mat("spark"), loop: true);
            var main = sparkles.main;
            main.startLifetime = 1.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.9f), new Color(0.6f, 1f, 0.6f));
            var emission = sparkles.emission;
            emission.rateOverTime = 14;
            var shape = sparkles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.9f;
            shape.rotation = new Vector3(90, 0, 0);
            Rise(sparkles, 1f, 2f);
            Fade(sparkles, Color.white, Color.white, 0.2f);
            Set(vfx, "height", 0f);
        }

        static void Lux(GameObject root, SkillVfx vfx) {
            Beam(root, vfx, Mat("beam", scroll: new Vector2(0, -6), color: new Color(1.6f, 1.45f, 1.1f, 1f)), 1.5f);
            var glow = Burst(root, "Origin", Mat("glow"), 1, 0.8f, 2.4f, new Color(1f, 0.95f, 0.7f), speed: 0, radius: 0, up: 0);
            var motes = Burst(root, "Motes", Mat("spark"), 40, 0.7f, 0.35f, new Color(1f, 0.9f, 0.6f), speed: 0.6f, radius: 0, up: 0);
            var shape = motes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.8f, 0.4f, 1f);
            shape.position = new Vector3(0, 0, 0.5f);
            var main = motes.main;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main = glow.main;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            Set(vfx, "beamFadeOut", 0.3f);
        }

        // ------------------------------------------------------------------ building blocks

        static void Build(string key, Action<GameObject, SkillVfx> recipe) {
            var path = $"{PrefabDir}/{key}.prefab";
            var root = new GameObject(key);
            try {
                var vfx = root.AddComponent<SkillVfx>();
                recipe(root, vfx);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally {
                Object.DestroyImmediate(root);
            }
        }

        static ParticleSystem Emitter(GameObject root, string name, Material material, bool loop = false) {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = loop;
            main.duration = loop ? 1 : 2;
            main.startSpeed = 0;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 200;
            var emission = ps.emission;
            emission.rateOverTime = 0;
            var shape = ps.shape;
            shape.enabled = false;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        /// <summary>One particle for the whole visual (a projectile's head).</summary>
        static ParticleSystem Single(GameObject root, string name, Material material, float lifetime, float size, Color color) {
            var ps = Emitter(root, name, material);
            var main = ps.main;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startColor = color;
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, 1) });
            return ps;
        }

        /// <summary>Particles left behind while moving (world space).</summary>
        static void Trail(GameObject root, string name, Material material, float perUnit, float lifetime, float size, Color start, Color end) {
            var ps = Emitter(root, name, material, loop: true);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startColor = start;
            var emission = ps.emission;
            emission.rateOverDistance = perUnit;
            Fade(ps, Color.white, end, 0.1f);
            var sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
        }

        /// <summary>A burst of particles flying out of a sphere.</summary>
        static ParticleSystem Burst(GameObject root, string name, Material material, int count, float lifetime, float size, Color color,
            float speed, float radius, float up, Color? endColor = null) {
            var ps = Emitter(root, name, material);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size);
            main.startSpeed = speed > 0 ? new ParticleSystem.MinMaxCurve(speed * 0.6f, speed) : 0;
            main.startColor = color;
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, count) });
            var shape = ps.shape;
            shape.enabled = radius > 0 || speed > 0;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(radius, 0.01f);
            if (up > 0) {
                Rise(ps, up * 0.6f, up);
            }
            Fade(ps, Color.white, endColor ?? Color.white, 0.05f);
            return ps;
        }

        /// <summary>Upward drift between <paramref name="min"/> and <paramref name="max"/>. Unity wants the x / y / z velocity
        /// curves in the same mode, so x and z get a (0, 0) range too.</summary>
        static ParticleSystem.VelocityOverLifetimeModule Rise(ParticleSystem ps, float min, float max) {
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0, 0);
            velocity.y = new ParticleSystem.MinMaxCurve(min, max);
            velocity.z = new ParticleSystem.MinMaxCurve(0, 0);
            return velocity;
        }

        /// <summary>A flat decal on the board, optionally growing to <paramref name="size"/>.</summary>
        static void Ground(GameObject root, string name, Material material, float lifetime, float size, Color color, bool grow) {
            var ps = Single(root, name, material, lifetime, size, color);
            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ps.transform.localPosition = Vector3.up * 0.05f;
            if (grow) {
                var sizeOver = ps.sizeOverLifetime;
                sizeOver.enabled = true;
                sizeOver.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 0.2f, 0, 3), new Keyframe(1, 1, 0, 0)));
            }
            Fade(ps, Color.white, Color.white, 0.05f);
        }

        /// <summary>Alpha in quickly, out to 0 at the end; tints toward <paramref name="end"/>.</summary>
        static void Fade(ParticleSystem ps, Color start, Color end, float fadeIn) {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(start, 0), new GradientColorKey(end, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, Mathf.Clamp(fadeIn, 0.01f, 0.5f)), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = gradient;
        }

        /// <summary>A quad along +Z from 0 to 1 (the VFX root is scaled to the beam length), flat on the board.</summary>
        static void Beam(GameObject root, SkillVfx vfx, Material material, float width) {
            var go = new GameObject("Beam");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0, 0, 0.5f);
            go.transform.localRotation = Quaternion.Euler(90, 0, 0);
            go.transform.localScale = new Vector3(width, 1, 1);
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Set(vfx, "beam", renderer);
        }

        static void Set(SkillVfx vfx, string field, object value) {
            var so = new SerializedObject(vfx);
            var p = so.FindProperty(field) ?? throw new ArgumentException(field);
            switch (value) {
                case float f: p.floatValue = f; break;
                case Object o: p.objectReferenceValue = o; break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ assets

        static readonly Dictionary<string, Material> materials = new();

        static Material Mat(string texture, bool additive = true, Vector2? scroll = null, Color? color = null) {
            var name = $"vfx_{texture}_{(additive ? "add" : "alpha")}{(scroll != null ? "_scroll" : "")}{(color != null ? $"_{ColorUtility.ToHtmlStringRGB(color.Value)}" : "")}";
            if (materials.TryGetValue(name, out var cached) && cached != null) return cached;
            var path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) {
                mat = new Material(Shader.Find("AutoChess/VFX Particle"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Shader.Find("AutoChess/VFX Particle");
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/vfx_{texture}.png"));
            mat.SetColor("_Color", color ?? Color.white);
            mat.SetVector("_Scroll", scroll ?? Vector2.zero);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            materials[name] = mat;
            return mat;
        }

        static void SetUpTextures() {
            foreach (var path in Directory.GetFiles(TextureDir, "*.png")) {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                var repeat = path.Contains("vfx_beam");
                var wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                if (importer.textureType == TextureImporterType.Default && importer.alphaIsTransparency && importer.wrapMode == wrap) continue;
                importer.textureType = TextureImporterType.Default;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.wrapMode = wrap;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
        }
    }
}
