using AutoChess.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>The unit shop: five offers, reroll, lock and the odds of the current level.</summary>
    public class ShopUI : BaseUI, ISessionView, ISessionResettable {
        [SerializeField] Shop_Hero[] heroes;
        [SerializeField] Button refreshButton;
        [SerializeField] TMP_Text refreshCostText;
        [SerializeField] Image lockedImage;
        [SerializeField] Sprite lockedSprite, unlockedSprite;
        [SerializeField] Button lockButton;
        [SerializeField] TMP_Text unknownRateText, eliteRateText, legendaryRateText;

        GameManager game;

        void Awake() {
            refreshButton.onClick.AddListener(Refresh);
            lockButton.onClick.AddListener(Lock);
        }

        public void Bind(GameManager gameManager) {
            game = gameManager;
            refreshCostText.text = $"<sprite name=coin>{game.Db.Rules.RefreshCost}";
            lockedImage.sprite = unlockedSprite;
            for (var i = 0; i < heroes.Length; i++) heroes[i].Bind(game, i);
            UpdateRates(1);
            game.EventRaised += OnEvent;
        }

        void OnDestroy() {
            if (game != null) game.EventRaised -= OnEvent;
        }

        public void OnSessionReset() {
            lockedImage.sprite = unlockedSprite;
            UpdateRates(1);
        }

        void OnEvent(GameEvent e) {
            if (e is PlayerEvent pe && pe.PlayerId != game.Session.LocalPlayerId) return;
            switch (e) {
                case ShopChangedEvent shop:
                    UpdateHeroes(shop.Rerolled);
                    lockedImage.sprite = shop.Locked ? lockedSprite : unlockedSprite;
                    break;
                case XpChangedEvent xp when xp.LeveledUp:
                    UpdateRates(xp.Level);
                    break;
            }
        }

        void UpdateHeroes(bool rerolled) {
            const float delayMul = 0.1f;
            var slots = game.Session.LocalPlayer.Shop.Slots;
            for (int i = 0; i < heroes.Length; i++) {
                var id = i < slots.Length ? slots[i] : null;
                if (rerolled) heroes[i].SetData(id == null ? null : HeroTraitDB.Instance.Find(id), i * delayMul);
                else if (id == null) heroes[i].MarkAsSold();
            }
        }

        void UpdateRates(int level) {
            var rates = game.Db.Rules.Level(level).ShopOdds;
            unknownRateText.text = $"{rates[0]}%";
            eliteRateText.text = $"{rates[1]}%";
            legendaryRateText.text = $"{rates[2]}%";
        }

        void Refresh() {
            game.Submit(new RefreshShopCommand());
        }

        void Lock() {
            game.Submit(new SetShopLockCommand { Locked = !game.Session.LocalPlayer.Shop.Locked });
        }
    }
}
