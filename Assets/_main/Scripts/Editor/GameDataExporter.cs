using System.IO;
using System.Linq;
using AutoChess.Core;
using UnityEditor;
using UnityEngine;

namespace AutoChess.Game.Editor {
    /// <summary>
    /// Writes GameData/game_data.json from the ScriptableObjects, with the same conversion the client uses at
    /// runtime. A dedicated server and the offline core tests (Tools/core_tests) load this file.
    /// </summary>
    public static class GameDataExporter {
        public const string RelativePath = "GameData/game_data.json";

        [MenuItem("AutoChess/Export Game Data")]
        public static void ExportMenu() {
            var path = Export();
            Debug.Log($"[AutoChess] game data exported to {path}");
        }

        /// <summary>Exports and returns the absolute path of the file.</summary>
        public static string Export() {
            var stages = FindStages();
            var board = Object.FindObjectOfType<BoardSpace>(true);
            var rules = new GameRules();
            var content = GameDataBuilder.BuildContent(stages,
                board != null ? board.CellWidth : rules.CellWidth,
                board != null ? board.CellHeight : rules.CellHeight);
            content.ToDatabase(); // validates (throws on broken references)

            var root = Directory.GetParent(Application.dataPath)!.FullName;
            var path = Path.Combine(root, RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, GameDataJson.Write(content) + "\n");
            return path;
        }

        /// <summary>The stages of the scene's GameManager, else every Stage asset by name.</summary>
        public static Stage[] FindStages() {
            var manager = Object.FindObjectOfType<GameManager>(true);
            if (manager != null) {
                var so = new SerializedObject(manager);
                var list = so.FindProperty("stages");
                if (list != null && list.arraySize > 0) {
                    return Enumerable.Range(0, list.arraySize)
                        .Select(i => list.GetArrayElementAtIndex(i).objectReferenceValue as Stage)
                        .Where(s => s != null).ToArray();
                }
            }
            return AssetDatabase.FindAssets("t:Stage")
                .Select(g => AssetDatabase.LoadAssetAtPath<Stage>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null).OrderBy(s => s.name).ToArray();
        }
    }
}
