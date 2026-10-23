using AutoChess.Net;
using TMPro;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>
    /// The Lobby scene's screen, built in code: player name, server address, play offline against bots, or find
    /// an online match (connect, queue, cancel). When the server starts the match <see cref="AppSession"/> loads
    /// the Arena; nothing about the match itself lives here.
    /// </summary>
    public class LobbyUI : MonoBehaviour {
        static readonly Color Muted = new(0.75f, 0.8f, 0.9f);
        static readonly Color Bad = new(0.95f, 0.45f, 0.4f);

        TMP_InputField nameField;
        TMP_InputField serverField;
        GameObject actions;
        GameObject queuePanel;
        TMP_Text status;
        TMP_Text queueText;
        TMP_Text cancelLabel;
        TMP_Text botsLabel;
        int bots;

        void Start() {
            bots = AppSession.OfflineBots;
            Build();
            Refresh();
        }

        void Update() => Refresh();

        void Build() {
            var ui = new UiBuilder(UiBuilder.FindFont());
            var root = UiBuilder.Canvas(transform, "LobbyCanvas", 10);
            var background = UiBuilder.Image(root, "Background", new Color(0.04f, 0.06f, 0.1f, 1f));
            UiBuilder.Stretch(background.rectTransform);

            var panel = ui.Panel(root, "Lobby", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720, 640), new Vector2(0.5f, 0.5f));
            var title = ui.Text(panel, "AUTO CHESS 2", 64, TextAlignmentOptions.Center, new Color(0.95f, 0.78f, 0.3f));
            UiBuilder.Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(660, 80), new Vector2(0.5f, 1));

            ui.Label(panel, "Tên người chơi", new Vector2(60, -130), 600);
            nameField = ui.Input(panel, "Name", AppSession.PlayerName, "Nhập tên", 20);
            UiBuilder.Place((RectTransform)nameField.transform, new Vector2(0.5f, 1), new Vector2(0, -160), new Vector2(600, 50), new Vector2(0.5f, 1));
            nameField.onEndEdit.AddListener(value => AppSession.PlayerName = value);

            ui.Label(panel, "Máy chủ", new Vector2(60, -225), 600);
            serverField = ui.Input(panel, "Server", AppSession.ServerUrl, AppSession.DefaultUrl);
            UiBuilder.Place((RectTransform)serverField.transform, new Vector2(0.5f, 1), new Vector2(0, -255), new Vector2(600, 50), new Vector2(0.5f, 1));
            serverField.onEndEdit.AddListener(value => AppSession.ServerUrl = value);

            // Choices (hidden while connecting / queued).
            actions = new GameObject("Actions", typeof(RectTransform));
            var actionsRect = (RectTransform)actions.transform;
            actionsRect.SetParent(panel, false);
            UiBuilder.Place(actionsRect, new Vector2(0.5f, 1), new Vector2(0, -330), new Vector2(600, 240), new Vector2(0.5f, 1));
            var online = ui.Button(actionsRect, "Tìm trận online", FindMatch, 28);
            UiBuilder.Place(online, new Vector2(0.5f, 1), Vector2.zero, new Vector2(600, 70), new Vector2(0.5f, 1));
            var hint = ui.Text(actionsRect, "8 người / trận. Đủ 2 người và chờ 30 giây thì bot vào thay chỗ trống.", 16,
                TextAlignmentOptions.Center, Muted);
            UiBuilder.Place(hint.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -76), new Vector2(600, 26), new Vector2(0.5f, 1));
            var offline = ui.Button(actionsRect, "Chơi với máy", PlayOffline, 28);
            UiBuilder.Place(offline, new Vector2(0, 1), new Vector2(0, -130), new Vector2(390, 70), new Vector2(0, 1));
            var fewer = ui.Button(actionsRect, "-", () => SetBots(bots - 1), 30);
            UiBuilder.Place(fewer, new Vector2(1, 1), new Vector2(-150, -130), new Vector2(50, 70), new Vector2(1, 1));
            botsLabel = ui.Text(actionsRect, "", 22, TextAlignmentOptions.Center, Color.white);
            UiBuilder.Place(botsLabel.rectTransform, new Vector2(1, 1), new Vector2(-55, -130), new Vector2(95, 70), new Vector2(1, 1));
            var more = ui.Button(actionsRect, "+", () => SetBots(bots + 1), 30);
            UiBuilder.Place(more, new Vector2(1, 1), Vector2.zero + new Vector2(0, -130), new Vector2(50, 70), new Vector2(1, 1));

            // Connecting / queued.
            var queue = ui.Panel(panel, "Queue", new Vector2(0.5f, 1), new Vector2(0, -330), new Vector2(600, 170), new Vector2(0.5f, 1));
            queue.GetComponent<UnityEngine.UI.Image>().color = UiBuilder.FieldColor;
            queuePanel = queue.gameObject;
            queueText = ui.Text(queue, "", 26, TextAlignmentOptions.Center, Color.white);
            UiBuilder.Place(queueText.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(560, 70), new Vector2(0.5f, 1));
            queueText.enableWordWrapping = true;
            var cancel = ui.Button(queue, "Huỷ", AppSession.CancelFind, 26);
            UiBuilder.Place(cancel, new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(240, 56), new Vector2(0.5f, 0));
            cancelLabel = cancel.GetComponentInChildren<TMP_Text>();

            status = ui.Text(panel, "", 20, TextAlignmentOptions.Center, Bad);
            UiBuilder.Place(status.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(660, 30), new Vector2(0.5f, 0));
        }

        void SetBots(int value) {
            bots = Mathf.Clamp(value, 1, 7);
        }

        void SaveFields() {
            AppSession.PlayerName = nameField.text;
            AppSession.ServerUrl = serverField.text;
        }

        void PlayOffline() {
            SaveFields();
            AppSession.PlayOffline(bots);
        }

        void FindMatch() {
            SaveFields();
            AppSession.FindMatch();
        }

        void Refresh() {
            if (actions == null) return;
            var online = AppSession.Online;
            var busy = online.State is OnlineState.Connecting or OnlineState.Queued or OnlineState.InMatch;
            actions.SetActive(!busy);
            queuePanel.SetActive(busy);
            nameField.interactable = serverField.interactable = !busy;
            botsLabel.text = $"{bots} bot";
            switch (online.State) {
                case OnlineState.Connecting:
                    queueText.text = "Đang kết nối máy chủ...";
                    cancelLabel.text = "Huỷ";
                    break;
                case OnlineState.Queued:
                    var seconds = Mathf.FloorToInt(AppSession.QueueSeconds);
                    queueText.text = $"Đang tìm trận  {seconds / 60}:{seconds % 60:00}\n" +
                                     $"<size=20>{online.QueueWaiting} người đang chờ (tối đa {Mathf.Max(online.MatchSize, 8)})</size>";
                    cancelLabel.text = "Huỷ tìm trận";
                    break;
                case OnlineState.InMatch:
                    queueText.text = "Đã tìm thấy trận! Đang vào...";
                    break;
            }
            // Errors (refused, unreachable server...) stay visible until the next attempt.
            status.text = online.State == OnlineState.Offline && !string.IsNullOrEmpty(online.Status) ? online.Status : "";
        }
    }
}
