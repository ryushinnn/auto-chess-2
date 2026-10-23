using AutoChess.Core;
using AutoChess.Core.Net;
using RExt.Patterns.Singleton;
using TMPro;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>
    /// What both scenes need before a match exists (Resources/ClientConfig): the stages of a match, the board
    /// cell size the simulation uses (must match the Arena's BoardSpace), and the UI font of the screens built in
    /// code. Builds the game database once per run; the lobby needs it (data hash for the server, the online
    /// replica is created there) and the Arena reuses it.
    /// </summary>
    public class ClientConfig : ScriptableObjectSingleton<ClientConfig> {
        [SerializeField] Stage[] stages;
        [Tooltip("Board cell size used by the simulation (BoardSpace's node size in the Arena scene).")]
        [SerializeField] float cellWidth = 1.88f;
        [SerializeField] float cellHeight = 1.89f;
        [Tooltip("Font of the HUD / lobby built in code (needs the Vietnamese glyphs).")]
        [SerializeField] TMP_FontAsset uiFont;

        GameDataJson.Content content;
        GameDatabase database;

        public Stage[] Stages => stages;
        public float CellWidth => cellWidth;
        public float CellHeight => cellHeight;
        public TMP_FontAsset UiFont => uiFont;

        /// <summary>The authoring data as engine-free definitions (also what AutoChess/Export Game Data writes).</summary>
        public GameDataJson.Content Content => content ??= GameDataBuilder.BuildContent(stages, cellWidth, cellHeight);

        public GameDatabase Database => database ??= Content.ToDatabase();

        /// <summary>Fingerprint the server compares with its exported game_data.json.</summary>
        public ulong DataHash => Lockstep.DataHash(GameDataJson.Write(Content));
    }
}
