using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

    public void SetData(Sprite hero, string name, Sprite item) {
        heroImage.sprite = hero;
        heroNameText.text = name;
        itemImage.sprite = item;
    }
    
    public void SetOnRefresh(Action onRefresh) {
        this.onRefresh = onRefresh;
    }

    public void SetOnSelect(Action onSelect) {
        this.onSelect = onSelect;
    }

    public void MarkAsSelected(bool selected) {
        backgroundImage.color = selected ? new Color(0.8f, 1, 0.5f) : Color.white;
        tween?.Kill();
        tween = heroImage.transform.DOScale(selected ? 1.1f : 1f, 0.2f);
    }
}