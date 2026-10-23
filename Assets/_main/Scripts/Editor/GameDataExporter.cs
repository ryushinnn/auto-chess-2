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
            // Fresh from the assets (ClientConfig caches its content for the running game).
            var config = ClientConfig.Instance;
            var content = GameDataBuilder.BuildContent(config.Stages, config.CellWidth, config.CellHeight);
            content.ToDatabase(); // validates (throws on broken references)

            var root = Directory.GetParent(Application.dataPath)!.FullName;
            var path = Path.Combine(root, RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, GameDataJson.Write(content) + "\n");
            return path;
        }
    }
}
