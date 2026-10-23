using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    public class Destinies_Destiny : MonoBehaviour {
        [SerializeField] Image iconImage;
        [SerializeField] TMP_Text nameText;
        [SerializeField] TMP_Text currentNumberText;
        [SerializeField] TMP_Text[] stageTexts;
        [SerializeField] TMP_Text[] separators;
        [SerializeField] Button button;

        public bool Empty { get; private set; } = true;

        Action onSelect;

        void Awake() {
            button.onClick.AddListener(() => onSelect?.Invoke());
        }

        public void SetData(string destinyName, Sprite icon, int current, int[] checkpoints, int index, Action onSelected) {
            onSelect = onSelected;
            nameText.text = destinyName;
            iconImage.sprite = icon;

            for (int i = 0; i < stageTexts.Length; i++) {
                if (i >= checkpoints.Length) {
                    stageTexts[i].gameObject.SetActive(false);
                    if (i > 0) separators[i - 1].gameObject.SetActive(false);
                    continue;
                }

                stageTexts[i].gameObject.SetActive(true);
                stageTexts[i].text = checkpoints[i].ToString();
                var reached = index >= i;
                stageTexts[i].color = reached ? (i == index ? Color.red : Color.white) : Color.gray;
                stageTexts[i].fontSize = reached && i == index ? 30 : 20;
                if (i > 0) {
                    separators[i - 1].gameObject.SetActive(true);
                    separators[i - 1].color = reached ? Color.white : Color.gray;
                }
            }
            currentNumberText.text = current.ToString();
            iconImage.color = index >= 0 ? Color.white : Color.gray;
            nameText.color = index >= 0 ? Color.white : Color.gray;

            Empty = false;
        }

        public void MarkAsEmpty() {
            Empty = true;
        }
    }
}
