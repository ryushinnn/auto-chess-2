using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>
    /// Small helpers to build plain uGUI screens in code (the match HUD, the lobby): overlay canvas, panels,
    /// texts, buttons and input fields with one font, laid out against a 1920x1080 reference.
    /// </summary>
    public sealed class UiBuilder {
        public static readonly Color PanelColor = new(0.05f, 0.07f, 0.11f, 0.84f);
        public static readonly Color ButtonColor = new(0.22f, 0.4f, 0.65f, 1f);
        public static readonly Color FieldColor = new(0.12f, 0.15f, 0.21f, 1f);

        public readonly TMP_FontAsset Font;

        public UiBuilder(TMP_FontAsset font) {
            Font = font;
        }

        /// <summary>The configured UI font, else a scene text's font that shows Vietnamese, else TMP's default.</summary>
        public static TMP_FontAsset FindFont() {
            var config = ClientConfig.Instance;
            if (config != null && config.UiFont != null) return config.UiFont;
            TMP_FontAsset any = null;
            foreach (var text in Object.FindObjectsOfType<TMP_Text>(true)) {
                if (text.font == null) continue;
                any ??= text.font;
                foreach (var c in text.text ?? "") {
                    if (c > 127) return text.font;
                }
            }
            return any != null ? any : TMP_Settings.defaultFontAsset;
        }

        /// <summary>A screen-space overlay canvas scaled from 1920x1080 (+ an EventSystem if the scene has none).</summary>
        public static RectTransform Canvas(Transform parent, string name, int sortingOrder) {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            if (Object.FindObjectOfType<EventSystem>() == null) {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(parent, false);
            }
            return (RectTransform)go.transform;
        }

        public static Image Image(Transform parent, string name, Color color) {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        public RectTransform Panel(RectTransform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null) {
            var image = Image(parent, name, PanelColor);
            Place(image.rectTransform, anchor, position, size, pivot ?? anchor);
            return image.rectTransform;
        }

        public TMP_Text Text(Transform parent, string text, float size, TextAlignmentOptions alignment, Color color) {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (Font != null) t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.alignment = alignment;
            t.color = color;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        public TMP_Text Label(RectTransform parent, string text, Vector2 position, float width = 280) {
            var label = Text(parent, text, 18, TextAlignmentOptions.TopLeft, new Color(0.75f, 0.8f, 0.9f));
            Place(label.rectTransform, new Vector2(0, 1), position, new Vector2(width, 26), new Vector2(0, 1));
            return label;
        }

        public RectTransform Button(RectTransform parent, string label, UnityAction onClick, float fontSize = 22) {
            var image = Image(parent, label, ButtonColor);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            var text = Text(image.rectTransform, label, fontSize, TextAlignmentOptions.Center, Color.white);
            Stretch(text.rectTransform);
            return image.rectTransform;
        }

        /// <summary>A single-line text field (TMP_InputField with its viewport, text and placeholder).</summary>
        public TMP_InputField Input(RectTransform parent, string name, string value, string placeholder, int characterLimit = 0) {
            var background = Image(parent, name, FieldColor);
            var field = background.gameObject.AddComponent<TMP_InputField>();
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(background.rectTransform, false);
            Stretch(viewport, 12, 12, 4, 4);
            var text = Text(viewport, "", 24, TextAlignmentOptions.Left, Color.white);
            text.overflowMode = TextOverflowModes.Overflow;
            Stretch(text.rectTransform);
            var hint = Text(viewport, placeholder, 24, TextAlignmentOptions.Left, new Color(1, 1, 1, 0.35f));
            Stretch(hint.rectTransform);
            field.textViewport = viewport;
            field.textComponent = text;
            field.placeholder = hint;
            field.fontAsset = Font;
            field.characterLimit = characterLimit;
            field.text = value;
            return field;
        }

        public static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null) {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot ?? anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        public static void Stretch(RectTransform rect, float left = 0, float right = 0, float top = 0, float bottom = 0) {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
