using System;
using AutoChess.Core;
using AutoChess.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutoChess.Game {
    /// <summary>
    /// What outlives the scenes: the connection to the auto-chess-2-server and the player's choice for the next
    /// match. The Lobby scene picks (offline vs bots, or find an online match); the Arena scene only plays and
    /// goes back to the Lobby when the match is over. A found online match loads the Arena by itself, wherever
    /// the player is.
    /// </summary>
    public static class AppSession {
        public const string LobbyScene = "Lobby";
        public const string ArenaScene = "Arena";
        public const string DefaultUrl = "ws://127.0.0.1:7777/ws";
        const string UrlKey = "AutoChess.ServerUrl";
        const string NameKey = "AutoChess.PlayerName";
        const string DeviceKey = "AutoChess.DeviceId";

        static OnlineClient online;
        static bool findWhenConnected;
        static float queuedSince;

        /// <summary>Computer players of the next offline match.</summary>
        public static int OfflineBots { get; set; } = 7;

        public static string PlayerName {
            get => PlayerPrefs.GetString(NameKey, "Player");
            set => PlayerPrefs.SetString(NameKey, string.IsNullOrWhiteSpace(value) ? "Player" : value.Trim());
        }

        public static string ServerUrl {
            get => PlayerPrefs.GetString(UrlKey, DefaultUrl);
            set => PlayerPrefs.SetString(UrlKey, string.IsNullOrWhiteSpace(value) ? DefaultUrl : value.Trim());
        }

        /// <summary>Seconds in the matchmaking queue (0 when not queued).</summary>
        public static float QueueSeconds => Online.State == OnlineState.Queued ? Time.unscaledTime - queuedSince : 0;

        /// <summary>The connection (created on first use, kept across scene loads).</summary>
        public static OnlineClient Online {
            get {
                if (online != null) return online;
                var config = ClientConfig.Instance;
                online = new OnlineClient(config.Database, config.DataHash);
                online.MatchStarted += _ => {
                    Debug.Log("[Online] match found, loading the Arena");
                    SceneManager.LoadScene(ArenaScene);
                };
                online.Log += message => Debug.Log($"[Online] {message}");
                var driver = new GameObject("AppSession");
                UnityEngine.Object.DontDestroyOnLoad(driver);
                driver.AddComponent<AppSessionDriver>();
                return online;
            }
        }

        /// <summary>The online match the Arena should show (found but not left yet), or null.</summary>
        public static LockstepSession PendingMatch =>
            online != null && online.State == OnlineState.InMatch ? online.Session : null;

        public static void PlayOffline(int bots) {
            OfflineBots = bots;
            SceneManager.LoadScene(ArenaScene);
        }

        /// <summary>Connects if needed, then queues for a match; the Arena loads when the server starts it.</summary>
        public static void FindMatch() {
            var client = Online;
            switch (client.State) {
                case OnlineState.Offline:
                    findWhenConnected = true;
                    client.Connect(ServerUrl, PlayerName, DeviceId());
                    break;
                case OnlineState.Lobby:
                    client.FindMatch();
                    queuedSince = Time.unscaledTime;
                    break;
            }
        }

        public static void CancelFind() {
            findWhenConnected = false;
            if (online == null) return;
            if (online.State == OnlineState.Queued) online.CancelFind();
            else if (online.State == OnlineState.Connecting) online.Disconnect();
        }

        /// <summary>Leaves the current match (online: the server keeps playing our roster) and opens the Lobby.</summary>
        public static void BackToLobby() {
            if (online != null && online.State == OnlineState.InMatch) online.LeaveMatch();
            SceneManager.LoadScene(LobbyScene);
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

        /// <summary>Pumps the connection every frame, in every scene.</summary>
        sealed class AppSessionDriver : MonoBehaviour {
            void Update() {
                online?.Update();
                if (findWhenConnected && online != null && online.State == OnlineState.Lobby) {
                    findWhenConnected = false;
                    online.FindMatch();
                    queuedSince = Time.unscaledTime;
                }
                if (online != null && online.State == OnlineState.Offline) findWhenConnected = false;
            }

            void OnApplicationQuit() => online?.Dispose();
        }
    }
}
