using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using Random = UnityEngine.Random;

public class Shop : MonoBehaviour {
    public event Action<HeroTrait[]> OnRefresh;
    
    [SerializeField, ReadOnly] HeroTrait[] heroes = new HeroTrait[SHOP_SLOTS_COUNT];
    [SerializeField, ReadOnly] bool lockAutoRefresh;
    [SerializeField, ReadOnly] int insurance;
    
    const int SHOP_SLOTS_COUNT = 5;
    
    public void Refresh(bool free) {
        if (!free) {
            if (GameManager.Instance.Inventory.Coins < GameConfigs.REFRESH_COST) return;
            GameManager.Instance.Inventory.SpendCoins(GameConfigs.REFRESH_COST);
        }
        
        for (int i=0; i<SHOP_SLOTS_COUNT; i++) {
            var rep = GetRandomReputation(GameManager.Instance.Level.LevelConfig.rates);
            if (rep == Reputation.Legendary) {
                insurance = 0;
            }
            else {
                insurance += GameManager.Instance.Level.LevelConfig.rates[^1];
                if (insurance >= 100) {
                    rep = Reputation.Legendary;
                    insurance = 0;
                }
            }
            var matchedHeroes = HeroTraitDB.Instance.FindAll(e => e.reputation == rep && !e.summoned);
            var randomHero = matchedHeroes[Random.Range(0, matchedHeroes.Count)];
            heroes[i] = randomHero;
        }
        OnRefresh?.Invoke(heroes);
    }

    public void AutoRefresh() {
        if (lockAutoRefresh) return;
        Refresh(true);
    }

    public bool Purchase(HeroTrait hero) {
        var price = GameConfigs.HERO_PRICES[hero.reputation];
        if (GameManager.Instance.Inventory.Coins < price) return false;
        if (GameManager.Instance.LineUp.Add(hero)) {
            GameManager.Instance.Inventory.SpendCoins(price);
            return true;
        }
        return false;
    }

    public bool SwitchLock(bool? value = null) {
        if (value.HasValue) {
            lockAutoRefresh = value.Value;
        }
        else {
            lockAutoRefresh = !lockAutoRefresh;
        }

        return lockAutoRefresh;
    }
    
    Reputation GetRandomReputation(int[] rates) {
        var random = Random.value;
        var totalRate = 0f;
        for (int i = 0; i < rates.Length; i++) {
            totalRate += rates[i] / 100f;
            if (random <= totalRate) {
                return (Reputation)(i + 1);
            }
        }
        return (Reputation)(rates.Length);
    }
}