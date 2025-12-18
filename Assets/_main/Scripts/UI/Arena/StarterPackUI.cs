using System;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

public class StarterPackUI : BaseUI {
    [SerializeField] StarterPack_Offer[] offers;
    [SerializeField] Button confirmButton;

    StarterPack_Offer selectedOffer;
    HeroTrait selectedHero;
    Item selectedItem;

    void Awake() {
        foreach (var offer in offers) {
            offer.SetOnRefresh(() => {
                if (selectedOffer == offer) {
                    selectedOffer.MarkAsSelected(false);
                    selectedOffer = null;
                }
                Refresh(offer);
            });
            Refresh(offer);
        }
        confirmButton.onClick.AddListener(Confirm);
    }

    void Refresh(StarterPack_Offer offer) {
        var matchedHeroes = HeroTraitDB.Instance.FindAll(e => e.reputation == Reputation.Unknown && !e.summoned);
        var hero = matchedHeroes[Random.Range(0, matchedHeroes.Count)];
        var item = ItemDB.Instance.GetRandomRawItem();
        offer.SetData(hero.thumbnail, hero.name, item.icon);
        offer.SetOnSelect(() => {
            if (selectedOffer == offer) return;
            selectedOffer?.MarkAsSelected(false);
            selectedOffer = offer;
            selectedOffer.MarkAsSelected(true);
            selectedHero = hero;
            selectedItem = item;
        });
    }

    void Confirm() {
        if (selectedHero == null || selectedItem == null) return;
        
        Close();
        UIManager_Arena.Instance.Arena.Open();
        GameManager.Instance.LineUp.Add(selectedHero);
        GameManager.Instance.Inventory.AddItem(selectedItem);
        GameManager.Instance.StartGame();
    }
}