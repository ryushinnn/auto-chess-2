using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AutoChess.Game.Editor {
    /// <summary>
    /// One-off (idempotent) fix-ups after the Core / view refactor: strips the components whose scripts were
    /// removed (rules that moved into AutoChess.Core), wires the new scene fields and lets 3D units receive
    /// EventSystem pointer events. Run it from the menu or through the AutoTest bridge (/menu).
    /// </summary>
    public static class ProjectMigration {
        const string ArenaScene = "Assets/_main/Scenes/Arena.unity";
        static readonly string[] Prefabs = {
            "Assets/_main/Prefabs/Hero.prefab",
            "Assets/_main/Prefabs/Hero_Battle.prefab",
            "Assets/_main/Prefabs/Hero_LineUp.prefab",
            "Assets/_main/Prefabs/DamageOverTimeArea.prefab",
        };

        [MenuItem("AutoChess/Migrate Scene and Prefabs")]
        public static void RunMenu() {
            Debug.Log(Run());
        }

        public static string Run() {
            var report = new StringBuilder("[AutoChess] migration\n");

            foreach (var path in Prefabs) {
                var root = PrefabUtility.LoadPrefabContents(path);
                var removed = 0;
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) {
                    removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                report.AppendLine($"  {path}: removed {removed} missing script(s)");
            }

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ArenaScene) scene = EditorSceneManager.OpenScene(ArenaScene, OpenSceneMode.Single);

            var missing = 0;
            foreach (var go in scene.GetRootGameObjects()) {
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) {
                    missing += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                }
            }
            report.AppendLine($"  {scene.path}: removed {missing} missing script(s)");

            var manager = Object.FindObjectOfType<GameManager>(true);
            if (manager != null) {
                var so = new SerializedObject(manager);
                var stages = so.FindProperty("stages");
                if (stages.arraySize == 0) {
                    var found = AssetDatabase.FindAssets("t:Stage")
                        .Select(g => AssetDatabase.LoadAssetAtPath<Stage>(AssetDatabase.GUIDToAssetPath(g)))
                        .Where(s => s != null).OrderBy(s => s.name).ToList();
                    stages.arraySize = found.Count;
                    for (var i = 0; i < found.Count; i++) stages.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
                    report.AppendLine($"  GameManager.stages <- {string.Join(", ", found.Select(s => s.name))}");
                }
                SetIfNull(so, "lineUp", Object.FindObjectOfType<RosterView>(true), report);
                SetIfNull(so, "battleField", Object.FindObjectOfType<BattleView>(true), report);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else {
                report.AppendLine("  !! no GameManager in the scene");
            }

            var cam = Camera.main;
            if (cam != null && cam.GetComponent<PhysicsRaycaster>() == null) {
                cam.gameObject.AddComponent<PhysicsRaycaster>();
                report.AppendLine($"  added PhysicsRaycaster to {cam.name}");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            report.Append("  saved");
            return report.ToString();
        }

        static void SetIfNull(SerializedObject so, string field, Object value, StringBuilder report) {
            var p = so.FindProperty(field);
            if (p == null || p.objectReferenceValue != null || value == null) return;
            p.objectReferenceValue = value;
            report.AppendLine($"  GameManager.{field} <- {value.name}");
        }

        /// <summary>Components in the open scenes and the hero prefabs whose script is missing.</summary>
        public static List<string> FindMissingScripts() {
            var result = new List<string>();
            foreach (var path in Prefabs) {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;
                foreach (var t in asset.GetComponentsInChildren<Transform>(true)) {
                    var n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (n > 0) result.Add($"{path}:{t.name} ({n})");
                }
            }
            for (var s = 0; s < EditorSceneManager.sceneCount; s++) {
                foreach (var go in EditorSceneManager.GetSceneAt(s).GetRootGameObjects()) {
                    foreach (var t in go.GetComponentsInChildren<Transform>(true)) {
                        var n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                        if (n > 0) result.Add($"{t.name} ({n})");
                    }
                }
            }
            return result;
        }
    }
}
