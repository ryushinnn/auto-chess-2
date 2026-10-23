using AutoChess.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>Pick one of the starter offers (a cheap unit + a component); each offer can be rerolled.</summary>
    public class StarterPackUI : BaseUI, ISessionView, ISessionResettable {
        [SerializeField] StarterPack_Offer[] offers;
        [SerializeField] Button confirmButton;

        GameManager game;
        int selected = -1;

        /// <summary>Selected offer index, or -1.</summary>
        public int Selected => selected;

        void Awake() {
            confirmButton.onClick.AddListener(Confirm);
        }

        public void Bind(GameManager gameManager) {
            game = gameManager;
            for (var i = 0; i < offers.Length; i++) {
                var index = i;
                offers[i].SetOnRefresh(() => Reroll(index));
                offers[i].SetOnSelect(() => Select(index));
            }
            game.EventRaised += OnEvent;
        }

        void OnDestroy() {
            if (game != null) game.EventRaised -= OnEvent;
        }

        public void OnSessionReset() {
            if (selected >= 0 && selected < offers.Length) offers[selected].MarkAsSelected(false);
            selected = -1;
        }

        void OnEvent(GameEvent e) {
            if (!(e is StarterOffersChangedEvent changed) || changed.PlayerId != game.Session.LocalPlayerId) return;
            if (changed.OfferIndex < 0) {
                for (var i = 0; i < offers.Length; i++) Show(i);
            }
            else {
                Show(changed.OfferIndex);
            }
        }

        void Show(int index) {
            if (index >= offers.Length) return;
            var offer = game.Session.LocalPlayer.StarterOffers[index];
            var hero = HeroTraitDB.Instance.Find(offer.UnitId);
            var item = ItemDB.Instance.Find(offer.ItemId);
            offers[index].SetData(hero != null ? hero.thumbnail : null, hero != null ? hero.name : "", item != null ? item.icon : null);
        }

        void Reroll(int index) {
            if (selected == index) {
                offers[index].MarkAsSelected(false);
                selected = -1;
            }
            game.Submit(new RerollStarterCommand { OfferIndex = index });
        }

        void Select(int index) {
            if (selected == index) return;
            if (selected >= 0) offers[selected].MarkAsSelected(false);
            selected = index;
            offers[selected].MarkAsSelected(true);
        }

        void Confirm() {
            if (selected < 0) return;
            // On success the match leaves the setup phase and UIManager_Arena swaps the screens.
            game.Submit(new ChooseStarterCommand { OfferIndex = selected });
        }
    }
}
