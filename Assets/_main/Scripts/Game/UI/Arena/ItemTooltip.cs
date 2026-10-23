using System.Collections.Generic;
using System.Text;
using AutoChess.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>
    /// Item details shown while the pointer is over an item (item bench, hero info): icon, name, recipe, stats and
    /// passive. Built in code on its own overlay canvas the first time it is needed.
    /// </summary>
    public sealed class ItemTooltip : MonoBehaviour {
        const float Width = 440;
        static ItemTooltip instance;

        RectTransform canvas;
        RectTransform panel;
        Image icon;
        TMP_Text title;
        TMP_Text body;
        readonly List<Image> recipe = new();

        static readonly Dictionary<StatKind, string> Sprites = new() {
            { StatKind.MaxHp, "hp" }, { StatKind.PhysicalDamage, "pdmg" }, { StatKind.MagicalDamage, "mdmg" },
            { StatKind.Armor, "arm" }, { StatKind.Resistance, "res" }, { StatKind.AttackSpeed, "aspd" },
            { StatKind.CriticalChance, "crc" }, { StatKind.CriticalDamage, "crd" }, { StatKind.EnergyRegenEfficiency, "ere" },
            { StatKind.PhysicalPenetration, "ppen" }, { StatKind.MagicalPenetration, "mpen" }, { StatKind.LifeSteal, "ls" },
            { StatKind.Tenacity, "ten" },
        };

        /// <summary>Stats stored as fractions even when flat (shown as percentages).</summary>
        static readonly HashSet<StatKind> Fractions = new() {
            StatKind.CriticalChance, StatKind.CriticalDamage, StatKind.EnergyRegenEfficiency, StatKind.PhysicalPenetration,
            StatKind.MagicalPenetration, StatKind.LifeSteal, StatKind.Tenacity,
        };

        /// <summary>Shows <paramref name="itemId"/> next to <paramref name="anchor"/> (a UI element).</summary>
        public static void Show(string itemId, RectTransform anchor) {
            var item = ItemDB.Instance.Find(itemId);
            if (item == null || anchor == null) return;
            if (instance == null) instance = Create();
            instance.Fill(item, anchor);
        }

        public static void Hide() {
            if (instance != null) instance.panel.gameObject.SetActive(false);
        }

        static ItemTooltip Create() {
            var root = new GameObject("ItemTooltip");
            var tooltip = root.AddComponent<ItemTooltip>();
            tooltip.Build();
            return tooltip;
        }

        void Build() {
            var ui = new UiBuilder(UiBuilder.FindFont());
            canvas = UiBuilder.Canvas(transform, "Canvas", 500);
            var panelImage = UiBuilder.Image(canvas, "Panel", new Color(0.05f, 0.07f, 0.11f, 0.95f));
            panelImage.raycastTarget = false;
            panel = panelImage.rectTransform;
            panel.pivot = new Vector2(0, 1);

            icon = UiBuilder.Image(panel, "Icon", Color.white);
            icon.raycastTarget = false;
            UiBuilder.Place(icon.rectTransform, new Vector2(0, 1), new Vector2(16, -16), new Vector2(64, 64), new Vector2(0, 1));

            title = ui.Text(panel, "", 26, TextAlignmentOptions.TopLeft, new Color(1f, 0.85f, 0.45f));
            UiBuilder.Place(title.rectTransform, new Vector2(0, 1), new Vector2(92, -16), new Vector2(Width - 108, 34), new Vector2(0, 1));

            for (var i = 0; i < 2; i++) {
                var part = UiBuilder.Image(panel, "Recipe" + i, Color.white);
                part.raycastTarget = false;
                UiBuilder.Place(part.rectTransform, new Vector2(0, 1), new Vector2(92 + i * 34, -52), new Vector2(28, 28), new Vector2(0, 1));
                recipe.Add(part);
            }

            body = ui.Text(panel, "", 19, TextAlignmentOptions.TopLeft, new Color(0.88f, 0.9f, 0.95f));
            body.enableWordWrapping = true;
            body.overflowMode = TextOverflowModes.Overflow;
            body.richText = true;
            UiBuilder.Place(body.rectTransform, new Vector2(0, 1), new Vector2(16, -92), new Vector2(Width - 32, 10), new Vector2(0, 1));
            panel.gameObject.SetActive(false);
        }

        void Fill(Item item, RectTransform anchor) {
            icon.sprite = item.icon;
            title.text = item.name;
            var forged = item.IsForgedItem();
            for (var i = 0; i < recipe.Count; i++) {
                var part = forged ? item.ingredients[i] : null;
                recipe[i].gameObject.SetActive(part != null);
                if (part != null) recipe[i].sprite = part.icon;
            }

            var text = new StringBuilder();
            foreach (var m in item.modifiers) {
                if (!StatKeys.TryParse(m.key, out var stat)) continue;
                text.Append("<color=#9FE870>+").Append(Format(stat, m.value, m.type == AttributeModifier.Type.Percentage)).Append("</color>");
                if (Sprites.TryGetValue(stat, out var sprite)) text.Append(" <sprite name=").Append(sprite).Append('>');
                text.Append('\n');
            }
            var passive = item.Description();
            if (passive.Length > 0) text.Append('\n').Append(passive);
            else if (!forged) text.Append("\n<color=#8C96A8><i>Nguyên liệu: ghép 2 món thành trang bị hoàn chỉnh.</i></color>");
            body.text = text.ToString().TrimEnd('\n');

            body.ForceMeshUpdate();
            var bodyHeight = Mathf.Max(20, body.preferredHeight);
            body.rectTransform.sizeDelta = new Vector2(Width - 32, bodyHeight);
            panel.sizeDelta = new Vector2(Width, 92 + bodyHeight + 18);
            Position(anchor);
            panel.gameObject.SetActive(true);
        }

        static string Format(StatKind stat, float value, bool percent) {
            if (percent || Fractions.Contains(stat)) return $"{value * 100:0.#}%";
            return $"{value:0.#}";
        }

        /// <summary>Beside the anchor (right of it, or left when there is no room), kept on screen.</summary>
        void Position(RectTransform anchor) {
            var corners = new Vector3[4];
            anchor.GetWorldCorners(corners);
            var anchorCanvas = anchor.GetComponentInParent<Canvas>();
            var cam = anchorCanvas != null && anchorCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? anchorCanvas.worldCamera : null;
            var topRight = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            var topLeft = RectTransformUtility.WorldToScreenPoint(cam, corners[1]);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, topRight, null, out var right);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, topLeft, null, out var left);
            var half = canvas.rect.size / 2;
            var size = panel.sizeDelta;
            var x = right.x + 12;
            if (x + size.x > half.x) x = left.x - 12 - size.x;
            var y = Mathf.Clamp(right.y, -half.y + size.y, half.y);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = new Vector2(Mathf.Clamp(x, -half.x, half.x - size.x), y);
        }

    }
}
