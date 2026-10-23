using System.Collections.Generic;
using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    public class ProgressBar : MonoBehaviour {
        [SerializeField] Image main;
        [SerializeField] Image sub;
        [SerializeField] Color positiveColor;
        [SerializeField] Color negativeColor;
        [SerializeField] bool ignoreAnimation;

        [SerializeField, ReadOnly] float amount;
        Sequence sequence;
        Image overlay;
        RectTransform ticks;
        readonly List<Image> tickImages = new();
        float tickTotal = -1;

        public float Amount => amount;

        public void UpdateAmount(float value) => UpdateAmount(value, false);

        public void UpdateAmount(float value, bool instantly) {
            if (instantly || ignoreAnimation) {
                amount = value;
                sequence?.Kill();
                main.fillAmount = amount;
                sub.fillAmount = amount;
                return;
            }

            if (value > amount) {
                amount = value;
                sub.color = positiveColor;
                sequence?.Kill();
                sequence = DOTween.Sequence()
                    .Append(sub.DOFillAmount(amount, 0.2f))
                    .Append(main.DOFillAmount(amount, 0.3f));
            }
            else if (value < amount) {
                amount = value;
                sub.color = negativeColor;
                sequence?.Kill();
                sequence = DOTween.Sequence()
                    .Append(main.DOFillAmount(amount, 0.2f))
                    .Append(sub.DOFillAmount(amount, 0.3f));
            }
        }

        public void SetMainColor(Color color) {
            main.color = color;
        }

        /// <summary>
        /// A second fill drawn behind the main one, ending at <paramref name="end"/> (0..1): the segment past the main
        /// fill shows in <paramref name="color"/> (shields on the health bar). 0 hides it.
        /// </summary>
        public void SetOverlay(float end, Color color) {
            if (overlay == null) {
                if (end <= 0) return;
                overlay = Instantiate(main, main.transform.parent);
                overlay.name = "Overlay";
                overlay.transform.SetSiblingIndex(main.transform.GetSiblingIndex());
            }
            overlay.color = color;
            overlay.fillAmount = Mathf.Clamp01(end);
            overlay.enabled = end > 0;
        }

        /// <summary>
        /// Tick marks over the bar for a scale of <paramref name="total"/>: a short one every <paramref name="step"/>, a
        /// full-height one every <paramref name="majorStep"/> (health bars: 100 / 1000 health).
        /// </summary>
        public void SetTicks(float total, float step, float majorStep) {
            total = Mathf.Round(total);
            if (Mathf.Approximately(total, tickTotal)) return;
            tickTotal = total;
            if (ticks == null) {
                ticks = new GameObject("Ticks", typeof(RectTransform)).GetComponent<RectTransform>();
                ticks.SetParent(main.transform.parent, false);
                var m = main.rectTransform;
                ticks.anchorMin = m.anchorMin;
                ticks.anchorMax = m.anchorMax;
                ticks.pivot = m.pivot;
                ticks.anchoredPosition = m.anchoredPosition;
                ticks.sizeDelta = m.sizeDelta;
                ticks.localRotation = m.localRotation;
                ticks.localScale = m.localScale;
            }
            ticks.SetAsLastSibling();
            var count = step > 0 && total > step ? Mathf.Min(200, Mathf.FloorToInt((total - 0.5f) / step)) : 0;
            var height = ticks.rect.height;
            for (var i = 0; i < count; i++) {
                if (i >= tickImages.Count) {
                    var image = new GameObject("Tick", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                    image.transform.SetParent(ticks, false);
                    image.raycastTarget = false;
                    tickImages.Add(image);
                }
                var tick = tickImages[i];
                var value = (i + 1) * step;
                var major = majorStep > 0 && Mathf.Abs(value / majorStep - Mathf.Round(value / majorStep)) < 1e-4f;
                var x = value / total;
                var rect = tick.rectTransform;
                rect.anchorMin = new Vector2(x, major ? 0 : 0.4f);
                rect.anchorMax = new Vector2(x, 1);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(Mathf.Max(0.5f, height * (major ? 0.26f : 0.14f)), 0);
                tick.color = new Color(0, 0, 0, major ? 0.95f : 0.7f);
                tick.gameObject.SetActive(true);
            }
            for (var i = count; i < tickImages.Count; i++) tickImages[i].gameObject.SetActive(false);
        }

        void OnDisable() {
            sequence?.Kill();
        }

        [Button]
        void Dev_UpdateAmount(float value) {
            UpdateAmount(value);
        }
    }
}
