using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    public class StarterPack_Offer : MonoBehaviour {
        [SerializeField] Image backgroundImage;
        [SerializeField] Image heroImage;
        [SerializeField] TMP_Text heroNameText;
        [SerializeField] Image itemImage;
        [SerializeField] Button refreshButton;
        [SerializeField] Button selectButton;

        Action onRefresh;
        Action onSelect;
        Tween tween;

        void Awake() {
            refreshButton.onClick.AddListener(() => onRefresh?.Invoke());
            selectButton.onClick.AddListener(() => onSelect?.Invoke());
        }

        public void SetData(Sprite hero, string heroName, Sprite item) {
            heroImage.sprite = hero;
            heroNameText.text = heroName;
            itemImage.sprite = item;
        }

        public void SetOnRefresh(Action refresh) {
            onRefresh = refresh;
        }

        public void SetOnSelect(Action select) {
            onSelect = select;
        }

        public void MarkAsSelected(bool selected) {
            backgroundImage.color = selected ? new Color(0.8f, 1, 0.5f) : Color.white;
            tween?.Kill();
            tween = heroImage.transform.DOScale(selected ? 1.1f : 1f, 0.2f);
        }
    }
}
