using System.Collections.Generic;
using AutoChess.Core;
using AutoChess.Core.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>
    /// TFT-style match HUD, built in code on its own overlay canvas (no scene / prefab edits), added by
    /// <see cref="GameManager"/> at runtime:
    /// <list type="bullet">
    /// <item>one panel, two tabs: the player list (health, level, streak, placement once out, the viewed player's
    /// opponent outlined in red; click = scout that board, Esc = home) and the damage meter (damage dealt by the viewed player's units in the current or
    /// last battle);</item>
    /// <item>round banner: stage-round, PvE / PvP and the opponent;</item>
    /// <item>toasts: damage taken, round income, sold units, eliminations;</item>
    /// <item>sell zone while a unit is dragged, and the placement screen when the match is over for us.</item>
    /// </list>
    /// </summary>
    public class MatchHud : MonoBehaviour, ISessionView, ISessionResettable {
        static readonly Color PanelColor = new(0.05f, 0.07f, 0.11f, 0.84f);
        static readonly Color RowColor = new(0.12f, 0.15f, 0.21f, 0.92f);
        static readonly Color LocalColor = new(0.85f, 0.65f, 0.18f, 1f);
        static readonly Color ViewedColor = new(0.35f, 0.75f, 1f, 1f);
        static readonly Color DeadColor = new(0.2f, 0.2f, 0.22f, 0.9f);
        static readonly Color GoodColor = new(0.35f, 0.85f, 0.4f);
        static readonly Color WarnColor = new(0.95f, 0.75f, 0.2f);
        static readonly Color BadColor = new(0.95f, 0.3f, 0.3f);
        const float RowHeight = 34;
        const float MeterRowHeight = 30;
        const int MaxMeterRows = 9;
        /// <summary>Tab bar height of the shared players / damage panel.</summary>
        const float TabsHeight = 40;

        sealed class PlayerWidget {
            public int PlayerId = -1;
            public Image Background;
            public Image Outline;
            public Image HpFill;
            public TMP_Text Name;
            public TMP_Text Hp;
            public TMP_Text Info;
            public RectTransform Rect;
        }

        sealed class MeterRow {
            public GameObject Root;
            public Image Fill;
            public TMP_Text Name;
            public TMP_Text Value;
        }

        GameManager game;
        TMP_FontAsset font;
        RectTransform root;

        readonly List<PlayerWidget> rows = new();
        RectTransform board;

        TMP_Text banner;
        GameObject scoutBar;
        TMP_Text scoutText;

        TMP_Text meterTitle;
        Image playersTab;
        Image damageTab;
        bool damageTabOpen;
        int playerCount;
        readonly List<MeterRow> meterRows = new();
        /// <summary>Health removed by each combat unit, for every battle of the round (battle id -> unit id -> amount).</summary>
        readonly Dictionary<int, Dictionary<int, double>> damage = new();
        /// <summary>Last battle of each player (kept through the preparation, like TFT's meter).</summary>
        readonly Dictionary<int, BattleSimulation> lastBattle = new();
        BattleSimulation meterBattle;
        TeamSide meterSide;
        bool meterDirty;

        RectTransform toastRoot;
        readonly List<(TMP_Text text, float until)> toasts = new();

        GameObject sellZone;
        TMP_Text sellText;

        GameObject endScreen;
        TMP_Text endTitle;
        TMP_Text endSubtitle;
        bool endShown;
        float nextRefresh;

        // ------------------------------------------------------------------ lifecycle

        public void Bind(GameManager gameManager) {
            game = gameManager;
            Build();
            game.EventRaised += OnEvent;
            game.ViewChanged += OnViewChanged;
            UnitDragHandler.PickStarted += OnPickStarted;
            UnitDragHandler.PickEnded += OnPickEnded;
            UnitDragHandler.OverSellZone = OverSellZone;
            OnSessionReset();
        }

        void OnDestroy() {
            if (game != null) {
                game.EventRaised -= OnEvent;
                game.ViewChanged -= OnViewChanged;
            }
            UnitDragHandler.PickStarted -= OnPickStarted;
            UnitDragHandler.PickEnded -= OnPickEnded;
            if (UnitDragHandler.OverSellZone == OverSellZone) UnitDragHandler.OverSellZone = null;
        }

        public void OnSessionReset() {
            foreach (var (text, _) in toasts) {
                if (text != null) Destroy(text.gameObject);
            }
            toasts.Clear();
            damage.Clear();
            lastBattle.Clear();
            meterBattle = null;
            if (scoutBar != null) scoutBar.SetActive(false);
            meterDirty = true;
            endShown = false;
            if (endScreen != null) endScreen.SetActive(false);
            if (sellZone != null) sellZone.SetActive(false);
            nextRefresh = 0;
        }

        void Update() {
            if (game == null || !game.IsReady || root == null) return;
            if (Time.unscaledTime >= nextRefresh) {
                nextRefresh = Time.unscaledTime + 0.2f;
                RefreshPlayers();
                RefreshBanner();
            }
            if (meterDirty) RefreshMeter();
            for (var i = toasts.Count - 1; i >= 0; i--) {
                var (text, until) = toasts[i];
                var left = until - Time.unscaledTime;
                if (left <= 0) {
                    Destroy(text.gameObject);
                    toasts.RemoveAt(i);
                    continue;
                }
                text.alpha = Mathf.Clamp01(left / 0.6f);
            }
        }

        // ------------------------------------------------------------------ events

        void OnEvent(GameEvent e) {
            var session = game.Session;
            var local = session.LocalPlayerId;
            switch (e) {
                case BattleCreatedEvent created:
                    if (!damage.ContainsKey(created.BattleId)) damage[created.BattleId] = new Dictionary<int, double>();
                    lastBattle[created.PlayerId] = session.State.BattleOf(created.PlayerId);
                    if (created.PlayerId == game.ViewedPlayerId) TrackBattle(created.PlayerId);
                    break;
                case UnitDamagedEvent hit when hit.SourceId >= 0 && damage.TryGetValue(hit.BattleId, out var dealt):
                    dealt.TryGetValue(hit.SourceId, out var total);
                    dealt[hit.SourceId] = total + hit.HpLost;
                    if (meterBattle != null && hit.BattleId == meterBattle.Id) meterDirty = true;
                    break;
                case PhaseChangedEvent phase when phase.Phase == MatchPhase.Transition:
                    damage.Clear(); // a new round: its battles are created right after this event
                    lastBattle.Clear();
                    nextRefresh = 0;
                    break;
                case PlayerDamagedEvent hurt when hurt.PlayerId == local:
                    Toast($"-{hurt.Amount} máu{(hurt.SourceId >= 0 ? $" ({NameOf(hurt.SourceId)})" : "")}", BadColor);
                    break;
                case IncomeEvent income when income.PlayerId == local && income.Total > 0:
                    var parts = $"lương {income.Base}";
                    if (income.Interest > 0) parts += $", lãi {income.Interest}";
                    if (income.Streak > 0) parts += $", chuỗi {income.Streak}";
                    Toast($"+{income.Total} vàng ({parts})", WarnColor);
                    break;
                case UnitSoldEvent sold when sold.PlayerId == local:
                    Toast($"Đã bán: +{sold.Gold} vàng", WarnColor);
                    break;
                case PlayerEliminatedEvent out_:
                    if (out_.PlayerId == local) ShowEnd(out_.Placement);
                    else Toast($"{NameOf(out_.PlayerId)} bị loại - hạng {out_.Placement}", Color.white);
                    break;
                case MatchFinishedEvent _:
                    var me = game.LocalPlayer;
                    if (me != null && !endShown) ShowEnd(me.Placement);
                    break;
                case PhaseChangedEvent _:
                    nextRefresh = 0;
                    break;
            }
        }

        void OnViewChanged(int playerId) {
            TrackBattle(playerId);
            scoutBar.SetActive(game.IsScouting);
            scoutText.text = game.IsScouting ? $"Đang xem nhà của {NameOf(playerId)}" : "";
            nextRefresh = 0;
        }

        /// <summary>The damage tab shows <paramref name="playerId"/>'s current battle, or their last one.</summary>
        void TrackBattle(int playerId) {
            lastBattle.TryGetValue(playerId, out var battle);
            meterBattle = battle;
            if (battle != null) meterSide = battle.Setup.Away.PlayerId == playerId && !battle.Setup.Away.Ghost ? TeamSide.Away : TeamSide.Home;
            meterDirty = true;
        }

        string NameOf(int playerId) => game.Session.State.Player(playerId)?.Name ?? $"#{playerId}";

        // ------------------------------------------------------------------ refresh

        void RefreshPlayers() {
            var state = game.Session.State;
            var players = state.Players;
            EnsureWidgets(players.Count);

            // Sorted like TFT's list: alive by health, then the eliminated by placement.
            var local = game.Session.LocalPlayerId;
            var viewed = state.Player(game.ViewedPlayerId);
            var fighting = state.Phase is MatchPhase.Transition or MatchPhase.Battle or MatchPhase.Summary;
            var opponentId = viewed != null && fighting ? viewed.OpponentId : -1;
            var order = new List<PlayerState>(players);
            order.Sort((a, b) => {
                if (a.Eliminated != b.Eliminated) return a.Eliminated ? 1 : -1;
                if (a.Eliminated) return a.Placement.CompareTo(b.Placement);
                return a.Health != b.Health ? b.Health.CompareTo(a.Health) : a.Id.CompareTo(b.Id);
            });
            for (var i = 0; i < order.Count; i++) {
                var p = order[i];
                var row = rows[i];
                row.PlayerId = p.Id;
                Paint(row, p, local);
                if (p.Id == opponentId && p.Id != game.ViewedPlayerId) row.Outline.color = viewed.OpponentIsGhost ? new Color(1, 1, 1, 0.5f) : BadColor;
                row.Name.text = Short(p.Name, 14);
                row.Hp.text = p.Eliminated ? $"#{p.Placement}" : p.Health.ToString();
                var streak = p.Streak >= 2 ? $"  <color=#ff9a3c>W{p.Streak}</color>" : p.Streak <= -2 ? $"  <color=#6fb7ff>L{-p.Streak}</color>" : "";
                row.Info.text = $"Lv{p.Level}{streak}";
                var fraction = p.Eliminated ? 0 : Mathf.Clamp01(p.Health / (float)game.Session.Db.Rules.StartingHealth);
                row.HpFill.rectTransform.anchorMax = new Vector2(fraction, 1);
                row.HpFill.color = fraction > 0.5f ? GoodColor * 0.55f : fraction > 0.25f ? WarnColor * 0.55f : BadColor * 0.55f;
            }
        }

        void Paint(PlayerWidget w, PlayerState p, int local) {
            w.Background.color = p.Eliminated ? DeadColor : RowColor;
            var outline = p.Id == game.ViewedPlayerId ? ViewedColor : p.Id == local ? LocalColor : Color.clear;
            w.Outline.color = outline;
            w.Name.color = p.Eliminated ? new Color(0.6f, 0.6f, 0.6f) : p.Id == local ? LocalColor : Color.white;
        }

        void RefreshBanner() {
            var session = game.Session;
            var state = session.State;
            var label = $"Vòng {state.StageIndex + 1}-{state.RoundIndex + 1}";
            var me = session.LocalPlayer;
            string detail;
            if (state.Phase == MatchPhase.Finished) detail = "Kết thúc";
            else if (state.Phase == MatchPhase.Setup) detail = "Chọn đội hình khởi đầu";
            else if (state.RoundKind == RoundKind.PvE) detail = "Đánh quái";
            else if (me != null && state.Phase != MatchPhase.Preparation && me.OpponentId >= 0)
                detail = $"Đấu với {NameOf(me.OpponentId)}{(me.OpponentIsGhost ? " (bản sao)" : "")}";
            else detail = "Đấu người";
            var hp = me != null && session.State.Players.Count > 1 ? $"   |   Máu {me.Health}" : "";
            banner.text = $"{label}   |   {detail}{hp}";
        }

        void RefreshMeter() {
            meterDirty = false;
            var entries = new List<(CombatUnit unit, double amount)>();
            if (meterBattle != null) {
                damage.TryGetValue(meterBattle.Id, out var dealt);
                foreach (var unit in meterBattle.Units) {
                    if (unit.Side != meterSide || unit.Def.Summoned) continue;
                    var amount = 0d;
                    dealt?.TryGetValue(unit.Id, out amount);
                    entries.Add((unit, amount));
                }
            }
            entries.Sort((a, b) => b.amount.CompareTo(a.amount));
            var max = entries.Count > 0 ? System.Math.Max(1, entries[0].amount) : 1;
            meterTitle.text = game.IsScouting ? $"Tướng của {NameOf(game.ViewedPlayerId)}" : "Tướng của bạn";
            if (entries.Count == 0) meterTitle.text += " - chưa có trận đấu";
            for (var i = 0; i < meterRows.Count; i++) {
                var row = meterRows[i];
                var show = damageTabOpen && i < entries.Count;
                row.Root.SetActive(show);
                if (!show) continue;
                var (unit, amount) = entries[i];
                row.Name.text = $"{Short(unit.Def.Name ?? unit.Def.Id, 12)} {Stars(unit.Rank)}";
                row.Value.text = Mathf.RoundToInt((float)amount).ToString();
                row.Fill.rectTransform.anchorMax = new Vector2((float)(amount / max), 1);
            }
            meterCount = Mathf.Min(entries.Count, MaxMeterRows);
            ResizePanel();
        }

        int meterCount;

        void ShowTab(bool damageTab) {
            damageTabOpen = damageTab;
            playersTab.color = damageTab ? RowColor : ViewedColor * 0.75f;
            this.damageTab.color = damageTab ? ViewedColor * 0.75f : RowColor;
            meterTitle.gameObject.SetActive(damageTab);
            for (var i = 0; i < rows.Count; i++) rows[i].Rect.gameObject.SetActive(!damageTab && i < playerCount);
            meterDirty = true;
            ResizePanel();
        }

        void ResizePanel() {
            var height = damageTabOpen
                ? TabsHeight + 30 + Mathf.Max(1, meterCount) * MeterRowHeight + 6
                : TabsHeight + playerCount * RowHeight + 6;
            board.sizeDelta = new Vector2(board.sizeDelta.x, height);
        }

        static string Stars(HeroRank rank) => rank switch { HeroRank.A => "**", HeroRank.S => "***", _ => "*" };

        static string Short(string text, int max) => string.IsNullOrEmpty(text) || text.Length <= max ? text : text.Substring(0, max - 1) + ".";

        void Toast(string message, Color color) {
            var text = MakeText(toastRoot, message, 26, TextAlignmentOptions.Center, color);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.sizeDelta = new Vector2(900, 34);
            toasts.Add((text, Time.unscaledTime + 3.5f));
            while (toasts.Count > 4) {
                Destroy(toasts[0].text.gameObject);
                toasts.RemoveAt(0);
            }
            for (var i = 0; i < toasts.Count; i++) toasts[i].text.rectTransform.anchoredPosition = new Vector2(0, -i * 36);
        }

        void ShowEnd(int placement) {
            endShown = true;
            endScreen.SetActive(true);
            var players = game.Session.State.Players.Count;
            if (players <= 1) {
                endTitle.text = game.LocalPlayer.Eliminated ? "Thất bại" : "Hoàn thành!";
                endSubtitle.text = "Bạn đã đi hết các vòng của chiến dịch.";
                return;
            }
            endTitle.text = placement == 1 ? "Chiến thắng!" : $"Hạng #{placement}";
            endSubtitle.text = placement == 1 ? $"Bạn trụ lại cuối cùng trong {players} người chơi."
                : placement <= 4 ? $"Top 4 trong {players} người chơi." : $"Bạn bị loại, xếp hạng {placement}/{players}.";
        }

        // ------------------------------------------------------------------ sell zone

        void OnPickStarted(RosterUnitView unit) {
            var owner = game.LocalPlayer;
            var model = owner?.FindUnit(unit.UnitId);
            if (model == null) return;
            sellText.text = $"Thả vào đây để bán: +{Core.Roster.SellValue(game.Db, model)} vàng";
            sellZone.SetActive(true);
        }

        void OnPickEnded(RosterUnitView unit) => sellZone.SetActive(false);

        bool OverSellZone(Vector2 screen) =>
            sellZone != null && sellZone.activeSelf && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)sellZone.transform, screen, null);

        // ------------------------------------------------------------------ building

        void Build() {
            font = FindFont();
            var canvasGo = new GameObject("MatchHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root = (RectTransform)canvasGo.transform;

            // Players / damage panel (top right, under the Online panel).
            board = Panel(root, "Players", new Vector2(1, 1), new Vector2(-12, -52), new Vector2(300, TabsHeight + 8 * RowHeight));
            playersTab = MakeButton(board, "Người chơi", () => ShowTab(false)).GetComponent<Image>();
            Place(playersTab.rectTransform, new Vector2(0, 1), new Vector2(8, -6), new Vector2(140, 30), new Vector2(0, 1));
            damageTab = MakeButton(board, "Sát thương", () => ShowTab(true)).GetComponent<Image>();
            Place(damageTab.rectTransform, new Vector2(1, 1), new Vector2(-8, -6), new Vector2(140, 30), new Vector2(1, 1));

            // Round banner and the scouting bar (top center).
            var bannerPanel = Panel(root, "Banner", new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(640, 44));
            banner = MakeText(bannerPanel, "", 26, TextAlignmentOptions.Center, Color.white);
            Stretch(banner.rectTransform);
            var scout = Panel(root, "Scouting", new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(640, 44));
            scoutBar = scout.gameObject;
            scoutText = MakeText(scout, "", 22, TextAlignmentOptions.Left, ViewedColor);
            Stretch(scoutText.rectTransform, 14, 170);
            var home = MakeButton(scout, "Về nhà (Esc)", () => game.ViewHome());
            Place(home, new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(150, 34));
            scoutBar.SetActive(false);

            // Damage tab: rows in the same panel, under a line naming whose units they are.
            meterTitle = Label(board, "", new Vector2(10, -TabsHeight - 2));
            for (var i = 0; i < MaxMeterRows; i++) {
                var rowRect = new GameObject($"Damage{i}", typeof(RectTransform)).GetComponent<RectTransform>();
                rowRect.SetParent(board, false);
                Place(rowRect, new Vector2(0, 1), new Vector2(8, -TabsHeight - 30 - i * MeterRowHeight), new Vector2(284, MeterRowHeight - 4), new Vector2(0, 1));
                var fill = MakeImage(rowRect, "Fill", new Color(0.85f, 0.35f, 0.2f, 0.75f));
                fill.rectTransform.anchorMin = Vector2.zero;
                fill.rectTransform.anchorMax = new Vector2(0, 1);
                fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                var name = MakeText(rowRect, "", 18, TextAlignmentOptions.Left, Color.white);
                Stretch(name.rectTransform, 6, 60);
                var value = MakeText(rowRect, "", 18, TextAlignmentOptions.Right, Color.white);
                Stretch(value.rectTransform, 6, 6);
                meterRows.Add(new MeterRow { Root = rowRect.gameObject, Fill = fill, Name = name, Value = value });
                rowRect.gameObject.SetActive(false);
            }
            ShowTab(false);

            // Toasts (upper middle).
            toastRoot = new GameObject("Toasts", typeof(RectTransform)).GetComponent<RectTransform>();
            toastRoot.SetParent(root, false);
            Place(toastRoot, new Vector2(0.5f, 1), new Vector2(0, -120), new Vector2(900, 160), new Vector2(0.5f, 1));

            // Sell zone (bottom middle, over the round timer) while a unit is dragged.
            var sell = Panel(root, "SellZone", new Vector2(0.5f, 0), new Vector2(0, 110), new Vector2(560, 90));
            sell.GetComponent<Image>().color = new Color(0.55f, 0.1f, 0.1f, 0.85f);
            sellText = MakeText(sell, "", 26, TextAlignmentOptions.Center, Color.white);
            Stretch(sellText.rectTransform);
            sellZone = sell.gameObject;
            sellZone.SetActive(false);

            // Placement screen.
            var dim = MakeImage(root, "EndScreen", new Color(0, 0, 0, 0.6f));
            Stretch(dim.rectTransform);
            endScreen = dim.gameObject;
            var endPanel = Panel(dim.rectTransform, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 300), new Vector2(0.5f, 0.5f));
            endTitle = MakeText(endPanel, "", 56, TextAlignmentOptions.Center, LocalColor);
            Place(endTitle.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(580, 70), new Vector2(0.5f, 1));
            endSubtitle = MakeText(endPanel, "", 24, TextAlignmentOptions.Center, Color.white);
            Place(endSubtitle.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -115), new Vector2(580, 60), new Vector2(0.5f, 1));
            var keepWatching = MakeButton(endPanel, "Xem tiếp", () => endScreen.SetActive(false));
            Place(keepWatching, new Vector2(0.5f, 0), new Vector2(-140, 30), new Vector2(240, 54), new Vector2(0.5f, 0));
            var again = MakeButton(endPanel, "Trận mới", () => {
                endScreen.SetActive(false);
                game.Restart();
            });
            Place(again, new Vector2(0.5f, 0), new Vector2(140, 30), new Vector2(240, 54), new Vector2(0.5f, 0));
            endScreen.SetActive(false);
        }

        void EnsureWidgets(int count) {
            while (rows.Count < count) {
                var row = Widget(board, $"Row{rows.Count}", new Vector2(284, RowHeight - 4));
                Place(row.Rect, new Vector2(0, 1), new Vector2(8, -TabsHeight - rows.Count * RowHeight), new Vector2(284, RowHeight - 4), new Vector2(0, 1));
                Stretch(row.Name.rectTransform, 8, 120);
                Stretch(row.Info.rectTransform, 160, 52);
                Stretch(row.Hp.rectTransform, 8, 8);
                rows.Add(row);
            }
            for (var i = 0; i < rows.Count; i++) rows[i].Rect.gameObject.SetActive(!damageTabOpen && i < count);
            if (count != playerCount) {
                playerCount = count;
                ResizePanel();
            }
        }

        PlayerWidget Widget(RectTransform parent, string name, Vector2 size) {
            var w = new PlayerWidget();
            w.Outline = MakeImage(parent, name, Color.clear);
            w.Rect = w.Outline.rectTransform;
            w.Rect.sizeDelta = size;
            w.Background = MakeImage(w.Rect, "Background", RowColor);
            Stretch(w.Background.rectTransform, 2, 2, 2, 2);
            w.HpFill = MakeImage(w.Background.rectTransform, "Hp", GoodColor * 0.55f);
            w.HpFill.rectTransform.anchorMin = Vector2.zero;
            w.HpFill.rectTransform.anchorMax = new Vector2(1, 1);
            w.HpFill.rectTransform.offsetMin = w.HpFill.rectTransform.offsetMax = Vector2.zero;
            w.Name = MakeText(w.Rect, "", 18, TextAlignmentOptions.Left, Color.white);
            w.Info = MakeText(w.Rect, "", 16, TextAlignmentOptions.Right, new Color(0.8f, 0.85f, 0.95f));
            w.Hp = MakeText(w.Rect, "", 18, TextAlignmentOptions.Right, Color.white);
            var button = w.Rect.gameObject.AddComponent<Button>();
            button.targetGraphic = w.Background;
            button.onClick.AddListener(() => {
                if (w.PlayerId >= 0) game.ViewPlayer(w.PlayerId);
            });
            return w;
        }

        RectTransform Panel(RectTransform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null) {
            var image = MakeImage(parent, name, PanelColor);
            Place(image.rectTransform, anchor, position, size, pivot ?? anchor);
            return image.rectTransform;
        }

        TMP_Text Label(RectTransform parent, string text, Vector2 position) {
            var label = MakeText(parent, text, 18, TextAlignmentOptions.TopLeft, new Color(0.75f, 0.8f, 0.9f));
            Place(label.rectTransform, new Vector2(0, 1), position, new Vector2(280, 26), new Vector2(0, 1));
            return label;
        }

        static Image MakeImage(Transform parent, string name, Color color) {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        TMP_Text MakeText(Transform parent, string text, float size, TextAlignmentOptions alignment, Color color) {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.alignment = alignment;
            t.color = color;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        RectTransform MakeButton(RectTransform parent, string label, UnityEngine.Events.UnityAction onClick) {
            var image = MakeImage(parent, label, new Color(0.22f, 0.4f, 0.65f, 1f));
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            var text = MakeText(image.rectTransform, label, 22, TextAlignmentOptions.Center, Color.white);
            Stretch(text.rectTransform);
            return image.rectTransform;
        }

        static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null) {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot ?? anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static void Stretch(RectTransform rect, float left = 0, float right = 0, float top = 0, float bottom = 0) {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>The font of a scene text that shows Vietnamese (the HUD's labels need its glyphs).</summary>
        static TMP_FontAsset FindFont() {
            TMP_FontAsset any = null;
            foreach (var text in FindObjectsOfType<TMP_Text>(true)) {
                if (text.font == null) continue;
                any ??= text.font;
                foreach (var c in text.text ?? "") {
                    if (c > 127) return text.font;
                }
            }
            return any != null ? any : TMP_Settings.defaultFontAsset;
        }
    }
}
