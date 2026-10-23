using AutoChess.Core;
using RExt.Patterns.Singleton;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>Owns the Arena screens and switches them with the match phase.</summary>
    public class UIManager_Arena : Singleton<UIManager_Arena>, ISessionView, ISessionResettable {
        public ArenaUI Arena => arena;
        public ShopUI Shop => shop;
        public DestiniesUI Destinies => destinies;
        public InventoryUI Inventory => inventory;
        public HeroInfoUI HeroInfo => heroInfo;
        public StarterPackUI StarterPack => starterPack;

        [SerializeField] ArenaUI arena;
        [SerializeField] ShopUI shop;
        [SerializeField] DestiniesUI destinies;
        [SerializeField] InventoryUI inventory;
        [SerializeField] HeroInfoUI heroInfo;
        [SerializeField] StarterPackUI starterPack;

        GameManager game;

        public void Bind(GameManager gameManager) {
            game = gameManager;
            CloseAll();
            game.EventRaised += OnEvent;
        }

        public void OnSessionReset() => CloseAll();

        void CloseAll() {
            arena.Close();
            shop.Close();
            destinies.Close();
            inventory.Close();
            heroInfo.Close();
            starterPack.Close();
        }

        void OnDestroy() {
            if (game != null) game.EventRaised -= OnEvent;
        }

        void OnEvent(GameEvent e) {
            if (!(e is PhaseChangedEvent phase)) return;
            if (phase.Phase == MatchPhase.Setup) {
                starterPack.Open();
            }
            else if (phase.Previous is MatchPhase.Setup or MatchPhase.None) {
                starterPack.Close();
                arena.Open();
            }
        }
    }
}
