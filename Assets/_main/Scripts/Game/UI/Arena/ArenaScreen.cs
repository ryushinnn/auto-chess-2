using AutoChess.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>
    /// The match screens, built in code in the style of <see cref="MatchHud"/> (dark panels, flat buttons) on their own
    /// overlay canvas, added by <see cref="GameManager"/> at runtime (was UIManager_Arena + the prefab panels):
    /// <list type="bullet">
    /// <item>the starter pick while the match is in setup (<see cref="StarterPanel"/>);</item>
    /// <item>then the HUD: synergies of the viewed board (left), item bench (left, drag onto a unit to equip, hold over
    /// another item to forge), level / xp (bottom left), the shop (bottom), gold (bottom right) and the details of a
    /// clicked unit (right, items can be taken off there).</item>
    /// </list>
    /// Everything is read from the session state, refreshed a few times a second and on its events.
    /// </summary>
    public class ArenaScreen : MonoBehaviour, ISessionView, ISessionResettable {
        public static ArenaScreen Instance { get; private set; }

        public ShopPanel Shop { get; private set; }
        public TraitsPanel Traits { get; private set; }
        public ItemBenchPanel Items { get; private set; }
        public HeroInfoPanel HeroInfo { get; private set; }
        public StarterPanel Starter { get; private set; }
        /// <summary>The HUD (everything but the starter pick) is showing.</summary>
        public bool HudOpen => hud != null && hud.gameObject.activeSelf;

        GameManager game;
        UiBuilder ui;
        RectTransform root;
        RectTransform hud;

        TMP_Text levelText;
        TMP_Text xpText;
        Image xpFill;
        TMP_Text boardText;
        TMP_Text buyXpText;
        Button buyXp;
        TMP_Text goldText;
        float nextRefresh;

        public void Bind(GameManager gameManager) {
            Instance = this;
            game = gameManager;
            Build();
            game.EventRaised += OnEvent;
            game.ViewChanged += OnViewChanged;
            OnSessionReset();
        }

        void OnDestroy() {
            if (Instance == this) Instance = null;
            if (game == null) return;
            game.EventRaised -= OnEvent;
            game.ViewChanged -= OnViewChanged;
        }

        public void OnSessionReset() {
            if (root == null) return;
            HeroInfo.Close();
            Starter.Reset();
            Tooltip.Hide();
            nextRefresh = 0;
            ShowFor(game.Session.State.Phase);
        }

        void OnEvent(GameEvent e) {
            switch (e) {
                case PhaseChangedEvent phase:
                    ShowFor(phase.Phase);
                    break;
                case StarterOffersChangedEvent offers when offers.PlayerId == game.Session.LocalPlayerId:
                    Starter.Refresh();
                    break;
                case PlayerEvent pe when pe.PlayerId == game.Session.LocalPlayerId || pe.PlayerId == game.ViewedPlayerId:
                    nextRefresh = 0;
                    break;
            }
            HeroInfo.OnEvent(e);
        }

        void OnViewChanged(int playerId) {
            HeroInfo.Close();
            nextRefresh = 0;
        }

        void ShowFor(MatchPhase phase) {
            var setup = phase == MatchPhase.Setup;
            Starter.SetOpen(setup);
            hud.gameObject.SetActive(!setup && phase != MatchPhase.None);
            if (setup) HeroInfo.Close();
            nextRefresh = 0;
        }

        void Update() {
            if (game == null || !game.IsReady || root == null) return;
            if (Time.unscaledTime < nextRefresh) {
                Items.Tick();
                return;
            }
            nextRefresh = Time.unscaledTime + 0.15f;
            if (HudOpen) {
                RefreshLevel();
                Shop.Refresh();
                Traits.Refresh();
                Items.Refresh();
                HeroInfo.Refresh();
            }
            Items.Tick();
        }

        void RefreshLevel() {
            var session = game.Session;
            var p = session.LocalPlayer;
            if (p == null) return;
            var rules = session.Db.Rules;
            var toNext = Economy.XpToNext(rules, p);
            levelText.text = $"Cấp {p.Level}";
            xpText.text = toNext < 0 ? "Tối đa" : $"{p.Xp}/{toNext} KN";
            xpFill.rectTransform.anchorMax = new Vector2(toNext <= 0 ? 1 : Mathf.Clamp01(p.Xp / (float)toNext), 1);
            var onBoard = 0;
            foreach (var u in p.Units) {
                if (u.Location.IsBoard) onBoard++;
            }
            var max = Economy.MaxBoardUnits(rules, p);
            boardText.text = $"Đội hình {onBoard}/{max}";
            boardText.color = onBoard > max ? UiBuilder.BadColor : onBoard == max ? Color.white : UiBuilder.GoldColor;
            buyXpText.text = $"Mua KN ({rules.XpPurchaseCost})";
            buyXp.interactable = toNext >= 0 && p.Gold >= rules.XpPurchaseCost && !p.Eliminated;
            goldText.text = $"{p.Gold}";
        }

        // ------------------------------------------------------------------ building

        void Build() {
            ui = new UiBuilder(UiBuilder.FindFont());
            root = UiBuilder.Canvas(transform, "ArenaHud", 30);
            hud = UiBuilder.Rect(root, "Hud");
            UiBuilder.Stretch(hud);

            Traits = new TraitsPanel(game, ui, hud);
            Items = new ItemBenchPanel(game, ui, hud, root);
            Shop = new ShopPanel(game, ui, hud);
            HeroInfo = new HeroInfoPanel(game, ui, hud);
            Starter = new StarterPanel(game, ui, root);

            // Level / xp / board size (bottom left).
            var level = ui.Panel(hud, "Level", new Vector2(0, 0), new Vector2(12, 12), new Vector2(260, 136));
            levelText = ui.Text(level, "", 30, TextAlignmentOptions.Left, Color.white);
            UiBuilder.Place(levelText.rectTransform, new Vector2(0, 1), new Vector2(14, -8), new Vector2(120, 38), new Vector2(0, 1));
            boardText = ui.Text(level, "", 18, TextAlignmentOptions.Right, Color.white);
            boardText.gameObject.name = "Board";
            UiBuilder.Place(boardText.rectTransform, new Vector2(1, 1), new Vector2(-12, -14), new Vector2(130, 28), new Vector2(1, 1));
            var xpBack = UiBuilder.Image(level, "Xp", UiBuilder.FieldColor);
            UiBuilder.Place(xpBack.rectTransform, new Vector2(0, 1), new Vector2(14, -52), new Vector2(232, 22), new Vector2(0, 1));
            xpFill = UiBuilder.Image(xpBack.rectTransform, "Fill", UiBuilder.AccentColor * 0.8f);
            xpFill.rectTransform.anchorMin = Vector2.zero;
            xpFill.rectTransform.anchorMax = new Vector2(0, 1);
            xpFill.rectTransform.offsetMin = xpFill.rectTransform.offsetMax = Vector2.zero;
            xpText = ui.Text(xpBack.rectTransform, "", 16, TextAlignmentOptions.Center, Color.white);
            UiBuilder.Stretch(xpText.rectTransform);
            var buy = ui.Button(level, "BuyXp", () => game.Submit(new BuyXpCommand()));
            UiBuilder.Place(buy, new Vector2(0, 0), new Vector2(14, 12), new Vector2(232, 44), new Vector2(0, 0));
            buyXp = buy.GetComponent<Button>();
            buyXpText = buy.GetComponentInChildren<TMP_Text>();

            // Gold (bottom right).
            var gold = ui.Panel(hud, "Gold", new Vector2(1, 0), new Vector2(-12, 12), new Vector2(170, 136));
            var caption = ui.Text(gold, "Vàng", 18, TextAlignmentOptions.Center, UiBuilder.MutedColor);
            UiBuilder.Place(caption.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(150, 26), new Vector2(0.5f, 1));
            goldText = ui.Text(gold, "", 56, TextAlignmentOptions.Center, UiBuilder.GoldColor);
            UiBuilder.Place(goldText.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(150, 70), new Vector2(0.5f, 0));
        }
    }
}
