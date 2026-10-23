using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AutoChess.Editor {
    /// <summary>Hero model setup helpers (glb model -> prefab + animator override + mecanim).</summary>
    public static class HeroModelTool {
        const string HeroModels = "Assets/_main/Models/Hero";

        /// <summary>Writes Temp/hero_models.txt: the clip mapping and mecanim timings of every set-up hero, plus the
        /// clips and renderers of every model.</summary>
        [MenuItem("AutoChess/Hero Models/Dump Setup")]
        public static void DumpSetup() {
            var sb = new StringBuilder();
            foreach (var dir in Directory.GetDirectories(HeroModels).OrderBy(d => d)) {
                var name = Path.GetFileName(dir);
                if (name.StartsWith("_")) continue;
                sb.AppendLine($"== {name}");
                foreach (var path in Directory.GetFiles(dir, "*.overrideController")) {
                    var oc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path.Replace('\\', '/'));
                    var overrides = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>();
                    oc.GetOverrides(overrides);
                    foreach (var o in overrides) {
                        sb.AppendLine($"  clip {o.Key.name} ({o.Key.length:0.###}s) -> {(o.Value ? $"{o.Value.name} ({o.Value.length:0.###}s)" : "-")}");
                    }
                }
                foreach (var path in Directory.GetFiles(dir, "*.prefab")) {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
                    foreach (var mb in prefab.GetComponents<MonoBehaviour>()) {
                        sb.AppendLine($"  component {mb.GetType().Name}: {EditorJsonUtility.ToJson(mb)}");
                    }
                    foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) {
                        if (!t.gameObject.activeSelf) sb.AppendLine($"  inactive {AnimationUtility.CalculateTransformPath(t, prefab.transform)}");
                    }
                    var anim = prefab.GetComponent<Animator>();
                    sb.AppendLine($"  animator {(anim ? anim.runtimeAnimatorController?.name : "none")}, scale {prefab.transform.localScale}");
                }
                foreach (var path in Directory.GetFiles(dir, "*.glb")) {
                    var p = path.Replace('\\', '/');
                    var clips = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview"));
                    sb.AppendLine("  glb clips: " + string.Join(", ", clips.Select(c => $"{c.name} {c.length:0.###}s")));
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    var renderers = model.GetComponentsInChildren<Renderer>(true);
                    sb.AppendLine($"  glb root {model.name}: animator {(model.GetComponent<Animator>() ? "yes" : "no")}, renderers: " +
                                  string.Join(", ", renderers.Select(r => $"{AnimationUtility.CalculateTransformPath(r.transform, model.transform)}[{r.bounds.size.y:0.##}]")));
                }
            }
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/hero_models.txt", sb.ToString());
            Debug.Log("hero models: Temp/hero_models.txt");
        }

        /// <summary>Writes Temp/hero_renderers.txt: every renderer of every hero model with its materials and whether the
        /// set-up prefab shows it.</summary>
        [MenuItem("AutoChess/Hero Models/Dump Renderers")]
        public static void DumpRenderers() {
            var sb = new StringBuilder();
            foreach (var dir in Directory.GetDirectories(HeroModels).OrderBy(d => d)) {
                var name = Path.GetFileName(dir);
                if (name.StartsWith("_")) continue;
                var prefabPath = Directory.GetFiles(dir, "*.prefab").FirstOrDefault();
                var glbPath = Directory.GetFiles(dir, "*.glb").FirstOrDefault();
                var root = prefabPath != null
                    ? AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath.Replace('\\', '/'))
                    : AssetDatabase.LoadAssetAtPath<GameObject>(glbPath?.Replace('\\', '/'));
                if (root == null) continue;
                sb.AppendLine($"== {name} ({(prefabPath != null ? "prefab" : "glb")}) {root.GetComponentsInChildren<Renderer>(true).Length} renderers");
                foreach (var r in root.GetComponentsInChildren<Renderer>(true)) {
                    var path = AnimationUtility.CalculateTransformPath(r.transform, root.transform);
                    var mats = string.Join("+", r.sharedMaterials.Select(m => m ? m.name : "null"));
                    var mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    var shown = true;
                    for (var t = r.transform; t != null; t = t.parent) shown &= t.gameObject.activeSelf;
                    sb.AppendLine($"  {(shown ? "ON " : "off")} {path} [{mats}] verts={(mesh ? mesh.vertexCount : 0)} size={r.bounds.size}");
                }
            }
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/hero_renderers.txt", sb.ToString());
            Debug.Log("hero renderers: Temp/hero_renderers.txt");
        }
    }
}
