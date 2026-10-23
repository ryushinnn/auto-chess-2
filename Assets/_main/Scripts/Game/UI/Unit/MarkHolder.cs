using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    public class MarkHolder : MonoBehaviour {
        public int Id => id;

        [SerializeField] Image iconImage;
        [SerializeField] Image timeLeftImage;
        [SerializeField] TMP_Text stacksText;

        int id;
        Tween tween;

        public void SetData(int markId, Sprite icon, int stacks, float duration) {
            SetData(markId, icon, stacks);
            tween = timeLeftImage.DOFillAmount(1, duration).SetEase(Ease.Linear);
        }

        public void SetData(int markId, Sprite icon, int stacks) {
            id = markId;
            iconImage.sprite = icon;
            stacksText.text = stacks.ToString();
            stacksText.gameObject.SetActive(stacks > 1);
            timeLeftImage.fillAmount = 0;
            tween?.Kill();
        }

        void OnDisable() {
            tween?.Kill();
        }
    }
}
