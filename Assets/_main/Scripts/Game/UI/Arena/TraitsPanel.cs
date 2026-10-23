using System.Collections.Generic;
using System.Text;
using AutoChess.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>Synergies (realms and roles) of the viewed board, active first; hovering a row shows its description.</summary>
    public sealed class TraitsPanel {
        const float Width = 260;
        const float RowHeight = 38;
        const float Header = 34;
        const int MaxRows = 14;

        sealed class Row {
            public RectTransform Rect;
            public Image Background;
            public Image Icon;
            public TMP_Text Name;
            public TMP_Text Count;
            public TMP_Text Tiers;
            public DestinyConfig Config;
            public int Tier;
            public int Members;
        }

        readonly GameManager game;
        readonly UiBuilder ui;
        readonly List<Row> rows = new();
        readonly TMP_Text title;
        string shown;

        public RectTransform Root { get; }

        public TraitsPanel(GameManager game, UiBuilder ui, RectTransform parent) {
            this.game = game;
            this.ui = ui;
            Root = ui.Panel(parent, "Traits", new Vector2(0, 1), new Vector2(12, -12), new Vector2(Width, Header));
            title = ui.Text(Root, "Tộc / Hệ", 18, TextAlignmentOptions.Left, UiBuilder.MutedColor);
            UiBuilder.Place(title.rectTransform, new Vector2(0, 1), new Vector2(12, -4), new Vector2(Width - 24, 26), new Vector2(0, 1));
        }

        public void Refresh() {
            var player = game.ViewedPlayer;
            if (player == null) return;
            var traits = Traits.ForBoard(game.Db, player);
            var key = new StringBuilder();
            foreach (var t in traits) key.Append(t.Id).Append(t.Count).Append(',');
            if (key.ToString() == shown) return;
            shown = key.ToString();

            var count = 0;
            foreach (var status in traits) {
                if (count >= MaxRows) break;
                var config = DestinyDB.Instance.Find(status.Id);
                if (config == null) continue;
                if (count >= rows.Count) rows.Add(BuildRow(count));
                Fill(rows[count], config, status);
                count++;
            }
            for (var i = 0; i < rows.Count; i++) rows[i].Rect.gameObject.SetActive(i < count);
            title.text = count == 0 ? "Chưa có tộc / hệ" : "Tộc / Hệ";
            Root.sizeDelta = new Vector2(Width, Header + count * RowHeight + (count > 0 ? 6 : 0));
        }

        Row BuildRow(int index) {
            var row = new Row();
            row.Background = UiBuilder.Image(Root, $"Trait{index}", UiBuilder.RowColor);
            row.Rect = row.Background.rectTransform;
            UiBuilder.Place(row.Rect, new Vector2(0, 1), new Vector2(8, -Header - index * RowHeight), new Vector2(Width - 16, RowHeight - 4), new Vector2(0, 1));
            row.Icon = UiBuilder.Icon(row.Rect, "Icon", null);
            UiBuilder.Place(row.Icon.rectTransform, new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(28, 28), new Vector2(0, 0.5f));
            row.Count = ui.Text(row.Rect, "", 20, TextAlignmentOptions.Center, Color.white);
            UiBuilder.Place(row.Count.rectTransform, new Vector2(0, 0.5f), new Vector2(34, 0), new Vector2(26, 30), new Vector2(0, 0.5f));
            row.Name = ui.Text(row.Rect, "", 17, TextAlignmentOptions.Left, Color.white);
            UiBuilder.Place(row.Name.rectTransform, new Vector2(0, 0.5f), new Vector2(62, 0), new Vector2(104, 30), new Vector2(0, 0.5f));
            row.Tiers = ui.Text(row.Rect, "", 15, TextAlignmentOptions.Right, UiBuilder.MutedColor);
            row.Tiers.richText = true;
            UiBuilder.Place(row.Tiers.rectTransform, new Vector2(1, 0.5f), new Vector2(-6, 0), new Vector2(80, 30), new Vector2(1, 0.5f));
            Hover.On(row.Background, () => {
                if (row.Config == null) return;
                Tooltip.ShowText(row.Config.GetIcon(), row.Config.GetName(), Describe(row.Config, row.Tier, row.Members), row.Rect);
            });
            return row;
        }

        static void Fill(Row row, DestinyConfig config, TraitStatus status) {
            row.Config = config;
            row.Tier = status.Tier;
            row.Members = status.Count;
            row.Icon.sprite = config.GetIcon();
            row.Name.text = config.GetName();
            row.Count.text = status.Count.ToString();
            var tiers = new StringBuilder();
            var checkpoints = config.checkpoints;
            for (var i = 0; i < checkpoints.Length; i++) {
                if (i > 0) tiers.Append(i <= status.Tier ? "<color=#FFFFFF>/</color>" : "/");
                tiers.Append(i == status.Tier ? $"<color=#F2C84B><b>{checkpoints[i]}</b></color>" : i < status.Tier ? $"<color=#FFFFFF>{checkpoints[i]}</color>" : checkpoints[i].ToString());
            }
            row.Tiers.text = tiers.ToString();
            var active = status.Tier >= 0;
            row.Background.color = active ? new Color(0.2f, 0.26f, 0.36f, 0.95f) : UiBuilder.RowColor;
            row.Icon.color = active ? Color.white : new Color(1, 1, 1, 0.45f);
            row.Name.color = active ? Color.white : UiBuilder.MutedColor;
            row.Count.color = active ? UiBuilder.GoldColor : UiBuilder.MutedColor;
        }

        /// <summary>Description with the reached tier highlighted, plus how many units count toward it.</summary>
        public static string Describe(DestinyConfig config, int tier, int members) {
            var text = config.Description(tier);
            if (members < 0) return text;
            var next = -1;
            foreach (var c in config.checkpoints) {
                if (c > members) {
                    next = c;
                    break;
                }
            }
            var status = tier >= 0 ? $"Đang kích hoạt mốc {config.checkpoints[tier]}" : "Chưa kích hoạt";
            if (next > 0) status += $" - cần {next - members} tướng nữa cho mốc {next}";
            return $"{text}\n\n<color=#8C96A8>{members} tướng - {status}</color>";
        }
    }
}
