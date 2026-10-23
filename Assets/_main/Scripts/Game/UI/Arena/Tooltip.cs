using System.Collections.Generic;
using System.Text;
using AutoChess.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>
    /// The hover card of the HUD, built in code on its own overlay canvas the first time it is needed: an item (icon,
    /// name, recipe, stats, passive) or any text (a trait, a skill), next to the element under the pointer, with an
    /// optional progress bar (holding an item over another to forge them).
    /// </summary>
    public sealed class Tooltip : MonoBehaviour {
        const float Width = 440;
        const float ProgressHeight = 10;
        static Tooltip instance;

        RectTransform canvas;
        RectTransform panel;
        Image icon;
        TMP_Text title;
        TMP_Text body;
        Image progressBack;
        Image progressFill;
        readonly List<Image> recipe = new();
        RectTransform anchor;
        float bodyHeight;

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

        public static bool Visible => instance != null && instance.panel.gameObject.activeSelf;

        /// <summary>Item details next to <paramref name="anchor"/> (a UI element).</summary>
        public static void ShowItem(string itemId, RectTransform anchor) {
            var item = ItemDB.Instance.Find(itemId);
            if (item == null || anchor == null) return;
            Instance.FillItem(item);
            instance.Open(anchor);
        }

        /// <summary>Free text (a trait, a skill) next to <paramref name="anchor"/>.</summary>
        public static void ShowText(Sprite sprite, string heading, string text, RectTransform anchor) {
            if (anchor == null) return;
            var t = Instance;
            t.icon.sprite = sprite;
            t.icon.enabled = sprite != null;
            t.title.text = heading;
            foreach (var part in t.recipe) part.gameObject.SetActive(false);
            t.body.text = text;
            t.Open(anchor);
        }

        /// <summary>The progress bar under the text, 0..1 (full = ready); null hides it.</summary>
        public static void SetProgress(float? value) {
            if (instance == null) return;
            var show = value.HasValue;
            if (instance.progressBack.gameObject.activeSelf != show) {
                instance.progressBack.gameObject.SetActive(show);
                instance.Layout();
            }
            if (!show) return;
            var v = Mathf.Clamp01(value.Value);
            instance.progressFill.rectTransform.anchorMax = new Vector2(v, 1);
            instance.progressFill.color = v >= 1 ? UiBuilder.GoodColor : UiBuilder.AccentColor;
        }

        public static void Hide() {
            if (instance == null) return;
            instance.panel.gameObject.SetActive(false);
            instance.progressBack.gameObject.SetActive(false);
            instance.anchor = null;
        }

        static Tooltip Instance {
            get {
                if (instance == null) {
                    var root = new GameObject("Tooltip");
                    instance = root.AddComponent<Tooltip>();
                    instance.Build();
                }
                return instance;
            }
        }

        void Build() {
            var ui = new UiBuilder(UiBuilder.FindFont());
            canvas = UiBuilder.Canvas(transform, "Canvas", 500);
            var panelImage = UiBuilder.Image(canvas, "Panel", new Color(0.05f, 0.07f, 0.11f, 0.95f));
            panelImage.raycastTarget = false;
            panel = panelImage.rectTransform;
            panel.pivot = new Vector2(0, 1);

            icon = UiBuilder.Icon(panel, "Icon", null);
            UiBuilder.Place(icon.rectTransform, new Vector2(0, 1), new Vector2(16, -16), new Vector2(64, 64), new Vector2(0, 1));

            title = ui.Text(panel, "", 26, TextAlignmentOptions.TopLeft, new Color(1f, 0.85f, 0.45f));
            title.enableWordWrapping = true;
            UiBuilder.Place(title.rectTransform, new Vector2(0, 1), new Vector2(92, -16), new Vector2(Width - 108, 34), new Vector2(0, 1));

            for (var i = 0; i < 2; i++) {
                var part = UiBuilder.Icon(panel, "Recipe" + i, null);
                UiBuilder.Place(part.rectTransform, new Vector2(0, 1), new Vector2(92 + i * 34, -52), new Vector2(28, 28), new Vector2(0, 1));
                recipe.Add(part);
            }

            body = ui.Text(panel, "", 19, TextAlignmentOptions.TopLeft, new Color(0.88f, 0.9f, 0.95f));
            body.enableWordWrapping = true;
            body.overflowMode = TextOverflowModes.Overflow;
            body.richText = true;
            UiBuilder.Place(body.rectTransform, new Vector2(0, 1), new Vector2(16, -92), new Vector2(Width - 32, 10), new Vector2(0, 1));

            progressBack = UiBuilder.Image(panel, "Progress", UiBuilder.FieldColor);
            progressBack.raycastTarget = false;
            progressFill = UiBuilder.Image(progressBack.rectTransform, "Fill", UiBuilder.AccentColor);
            progressFill.raycastTarget = false;
            progressFill.rectTransform.anchorMin = Vector2.zero;
            progressFill.rectTransform.anchorMax = new Vector2(0, 1);
            progressFill.rectTransform.offsetMin = progressFill.rectTransform.offsetMax = Vector2.zero;
            progressBack.gameObject.SetActive(false);
            panel.gameObject.SetActive(false);
        }

        void FillItem(Item item) {
            icon.sprite = item.icon;
            icon.enabled = true;
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
            else if (!forged) text.Append("\n<color=#8C96A8><i>Nguyên liệu: giữ kéo lên một nguyên liệu khác để ghép, hoặc thả lên tướng.</i></color>");
            body.text = text.ToString().TrimEnd('\n');
        }

        void Open(RectTransform target) {
            anchor = target;
            Layout();
            panel.gameObject.SetActive(true);
        }

        /// <summary>Sizes the card to its text (and the progress bar) and places it beside the anchor.</summary>
        void Layout() {
            var hasIcon = icon.enabled;
            var textLeft = hasIcon ? 92f : 16f;
            title.rectTransform.anchoredPosition = new Vector2(textLeft, -16);
            title.rectTransform.sizeDelta = new Vector2(Width - textLeft - 16, 34);
            title.ForceMeshUpdate();
            var titleHeight = Mathf.Max(34, title.preferredHeight);
            title.rectTransform.sizeDelta = new Vector2(Width - textLeft - 16, titleHeight);
            var top = hasIcon ? Mathf.Max(92, 16 + titleHeight + (recipe[0].gameObject.activeSelf ? 46 : 10)) : 16 + titleHeight + 10;
            body.rectTransform.anchoredPosition = new Vector2(16, -top);
            body.ForceMeshUpdate();
            bodyHeight = string.IsNullOrEmpty(body.text) ? 0 : Mathf.Max(20, body.preferredHeight);
            body.rectTransform.sizeDelta = new Vector2(Width - 32, bodyHeight);
            var height = top + bodyHeight + 18;
            if (progressBack.gameObject.activeSelf) {
                UiBuilder.Place(progressBack.rectTransform, new Vector2(0, 1), new Vector2(16, -height + 4), new Vector2(Width - 32, ProgressHeight), new Vector2(0, 1));
                height += ProgressHeight + 12;
            }
            panel.sizeDelta = new Vector2(Width, height);
            if (anchor != null) Position(anchor);
        }

        static string Format(StatKind stat, float value, bool percent) {
            if (percent || Fractions.Contains(stat)) return $"{value * 100:0.#}%";
            return $"{value:0.#}";
        }

        /// <summary>Beside the anchor (right of it, or left when there is no room), kept on screen.</summary>
        void Position(RectTransform target) {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var anchorCanvas = target.GetComponentInParent<Canvas>();
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
