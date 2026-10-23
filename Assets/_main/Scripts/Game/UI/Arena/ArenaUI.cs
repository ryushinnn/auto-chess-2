using AutoChess.Core;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>Main HUD: gold, level / xp, board count, stage and phase timer, panel toggles.</summary>
    public class ArenaUI : BaseUI, ISessionView {
        [SerializeField] Button lineUpButton;
        [SerializeField] Button inventoryButton;
        [SerializeField] Button shopButton;
        [SerializeField] TMP_Text coinsText;
        [SerializeField] Button buyXpButton;
        [SerializeField] TMP_Text levelText;
        [SerializeField] TMP_Text xpText;
        [SerializeField] TMP_Text lineUpText;
        [SerializeField] Image timeLeftImage;
        [SerializeField] TMP_Text roundAndWaveText;
        [SerializeField] TMP_Text timeLeftText;
        [SerializeField] DOTweenAnimation[] animations;

        GameManager game;
        int shownSeconds = -1;

        void Awake() {
            shopButton.onClick.AddListener(SwitchShop);
            lineUpButton.onClick.AddListener(OpenLineUp);
            inventoryButton.onClick.AddListener(OpenInventory);
            buyXpButton.onClick.AddListener(BuyXp);
        }

        public void Bind(GameManager gameManager) {
            game = gameManager;
            game.EventRaised += OnEvent;
        }

        void OnDestroy() {
            if (game != null) game.EventRaised -= OnEvent;
        }

        public override void Open() {
            base.Open();
            foreach (var anim in animations) {
                anim.DOPlay();
            }
        }

        void OnEvent(GameEvent e) {
            if (e is PlayerEvent pe && pe.PlayerId != game.Session.LocalPlayerId) return;
            switch (e) {
                case GoldChangedEvent gold:
                    coinsText.text = $"{gold.Gold}<sprite name=coin>";
                    break;
                case XpChangedEvent xp:
                    levelText.text = $"Lv.{xp.Level}";
                    xpText.text = xp.XpToNext < 0 ? "MAX" : $"{xp.Xp}/{xp.XpToNext}";
                    break;
                case BoardLimitChangedEvent board:
                    lineUpText.text = $"{board.Count}/{board.Max}";
                    lineUpText.color = board.Count >= board.Max ? Color.red : Color.white;
                    break;
                case PhaseChangedEvent phase:
                    roundAndWaveText.text = $"{phase.Stage + 1}-{phase.Round + 1}";
                    shownSeconds = -1;
                    break;
            }
        }

        void Update() {
            if (game == null || !game.IsReady) return;
            var session = game.Session;
            var total = session.PhaseDuration;
            var left = session.PhaseTimeLeft;
            timeLeftImage.fillAmount = total > 0 ? left / total : 0;
            var seconds = Mathf.CeilToInt(left);
            if (seconds != shownSeconds) {
                shownSeconds = seconds;
                timeLeftText.SetText("{0}", seconds);
            }
        }

        void SwitchShop() {
            var shop = UIManager_Arena.Instance.Shop;
            if (shop.IsOpen) shop.Close();
            else shop.Open();
        }

        void OpenLineUp() {
            UIManager_Arena.Instance.Destinies.Open();
            UIManager_Arena.Instance.Inventory.Close();
        }

        void OpenInventory() {
            UIManager_Arena.Instance.Destinies.Close();
            UIManager_Arena.Instance.Inventory.Open();
        }

        void BuyXp() {
            game.Submit(new BuyXpCommand());
        }
    }
}
