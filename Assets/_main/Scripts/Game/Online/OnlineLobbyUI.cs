using System;
using AutoChess.Net;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>
    /// Small IMGUI panel (top right) for online play: server address, name, connect, find a match, leave.
    /// Added at runtime by <see cref="GameManager"/>; offline play is untouched until the player connects.
    /// </summary>
    public class OnlineLobbyUI : MonoBehaviour {
        public const string DefaultUrl = "ws://127.0.0.1:7777/ws";
        const string UrlKey = "AutoChess.ServerUrl";
        const string NameKey = "AutoChess.PlayerName";
        const string DeviceKey = "AutoChess.DeviceId";
        const float Width = 260;

        GameManager game;
        string url;
        string playerName;
        bool expanded;

        void Start() {
            game = GetComponent<GameManager>();
            url = PlayerPrefs.GetString(UrlKey, DefaultUrl);
            playerName = PlayerPrefs.GetString(NameKey, "Player");
        }

        /// <summary>Stable id of this install; "-deviceId X" on the command line overrides it (several local clients).</summary>
        static string DeviceId() {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++) {
                if (args[i] == "-deviceId") return args[i + 1];
            }
            if (!PlayerPrefs.HasKey(DeviceKey)) PlayerPrefs.SetString(DeviceKey, Guid.NewGuid().ToString("N"));
            return PlayerPrefs.GetString(DeviceKey);
        }

        void OnGUI() {
            var online = game != null ? game.Online : null;
            if (online == null) return;
            var scale = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var area = new Rect(Screen.width / scale - Width - 10, 10, Width, expanded ? 230 : 34);
            GUILayout.BeginArea(area, GUI.skin.box);
            if (GUILayout.Button($"{(expanded ? "▾" : "▸")} Online: {online.State}")) expanded = !expanded;
            if (expanded) DrawBody(online);
            GUILayout.EndArea();
        }

        void DrawBody(OnlineClient online) {
            var match = game.OnlineSession;
            switch (online.State) {
                case OnlineState.Offline:
                    GUILayout.Label("Server");
                    url = GUILayout.TextField(url);
                    GUILayout.Label("Name");
                    playerName = GUILayout.TextField(playerName, 20);
                    if (GUILayout.Button("Connect")) {
                        PlayerPrefs.SetString(UrlKey, url);
                        PlayerPrefs.SetString(NameKey, playerName);
                        online.Connect(url.Trim(), playerName, DeviceId());
                    }
                    break;
                case OnlineState.Connecting:
                    if (GUILayout.Button("Cancel")) online.Disconnect();
                    break;
                case OnlineState.Lobby:
                    if (GUILayout.Button("Find match")) online.FindMatch();
                    if (GUILayout.Button("Disconnect")) online.Disconnect();
                    break;
                case OnlineState.Queued:
                    if (GUILayout.Button("Cancel")) online.CancelFind();
                    break;
                case OnlineState.InMatch when match != null:
                    GUILayout.Label($"Match {match.Info.MatchId} - you are {match.Info.Names[match.LocalPlayerId]}");
                    GUILayout.Label(string.Join(", ", match.Info.Names));
                    GUILayout.Label($"frame {match.Frame}, buffered {match.BufferedFrames}");
                    if (match.IsDesynced) GUILayout.Label("<color=red>out of sync with the server</color>");
                    if (GUILayout.Button("Leave match")) game.Restart();
                    break;
            }
            if (match != null && online.State != OnlineState.InMatch && GUILayout.Button("Back to offline")) game.Restart();
            if (!string.IsNullOrEmpty(online.Status)) GUILayout.Label(online.Status);
        }
    }
}
