using System;
using System.Collections.Generic;
using RExt.Utils;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

public class ShopUI : BaseUI {
    [SerializeField] Shop_Hero[] heroes;
    [SerializeField] Button refreshButton;
    [SerializeField] TMP_Text refreshCostText;
    [SerializeField] Image lockedImage;
    [SerializeField] Sprite lockedSprite, unlockedSprite;
    [SerializeField] Button lockButton;
    [SerializeField] TMP_Text unknownRateText, eliteRateText, legendaryRateText;
    
    void Awake() {
        refreshButton.onClick.AddListener(Refresh);
        lockButton.onClick.AddListener(Lock);
        refreshCostText.text = $"<sprite name=coin>{GameConfigs.REFRESH_COST}";
        lockedImage.sprite = unlockedSprite;
        UpdateRates(1);
    }

    void Start() {
        GameManager.Instance.Shop.OnRefresh += UpdateHeroes;
        GameManager.Instance.Level.OnLevelUp += UpdateRates;
    }

    void UpdateHeroes(HeroTrait[] traits) {
        var delayMul = 0.1f;
        for (int i = 0; i < heroes.Length; i++) {
            heroes[i].SetData(traits[i], i * delayMul);
        }
    }

    void UpdateRates(int level) {
        var rates = GameConfigs.LEVEL_CONFIGS[level - 1].rates;
        unknownRateText.text = $"{rates[0]}%";
        eliteRateText.text = $"{rates[1]}%";
        legendaryRateText.text = $"{rates[2]}%";
    }

    void Refresh() {
        GameManager.Instance.Shop.Refresh(false);
        GameManager.Instance.Shop.SwitchLock(false);
        lockedImage.sprite = unlockedSprite;
    }

    void Lock() {
        var locked = GameManager.Instance.Shop.SwitchLock();
        lockedImage.sprite = locked ? lockedSprite : unlockedSprite;
    }
}