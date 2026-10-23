using System.Collections.Generic;
using System.Text;
using AutoChess.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>The unit shop along the bottom: five offers (click buys), refresh, lock and the odds of the level.</summary>
    public sealed class ShopPanel {
        const float CardWidth = 214;
        const float CardHeight = 120;
        const float Gap = 8;
        const float SideWidth = 150;

        public sealed class Card {
            public int Slot;
            public string UnitId;
            public RectTransform Rect;
            public Image Background;
            public Image Thumbnail;
            public TMP_Text Name;
            public TMP_Text Cost;
            public TMP_Text Traits;
            public GameObject Sold;
            public Button Button;
        }

        static readonly Role[] RoleBuffer = new Role[6];

        readonly GameManager game;
        readonly List<Card> cards = new();
        readonly TMP_Text refreshText;
        readonly TMP_Text lockText;
        readonly Image lockImage;
        readonly TMP_Text oddsText;
        string shown;

        public RectTransform Root { get; }
        public IReadOnlyList<Card> Cards => cards;

        public ShopPanel(GameManager game, UiBuilder ui, RectTransform parent) {
            this.game = game;
            var slots = game.Db.Rules.ShopSlots;
            var width = slots * CardWidth + (slots - 1) * Gap + SideWidth + 28;
            Root = ui.Panel(parent, "Shop", new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(width, CardHeight + 16));
            for (var i = 0; i < slots; i++) cards.Add(BuildCard(ui, i));

            var side = UiBuilder.Rect(Root, "Side");
            UiBuilder.Place(side, new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(SideWidth, CardHeight), new Vector2(1, 0.5f));
            oddsText = ui.Text(side, "", 15, TextAlignmentOptions.Center, UiBuilder.MutedColor);
            oddsText.richText = true;
            UiBuilder.Place(oddsText.rectTransform, new Vector2(0.5f, 1), Vector2.zero, new Vector2(SideWidth, 22), new Vector2(0.5f, 1));
            var refresh = ui.Button(side, "Refresh", () => game.Submit(new RefreshShopCommand()), 18);
            UiBuilder.Place(refresh, new Vector2(0.5f, 1), new Vector2(0, -26), new Vector2(SideWidth, 44), new Vector2(0.5f, 1));
            refreshText = refresh.GetComponentInChildren<TMP_Text>();
            var lockButton = ui.Button(side, "Lock", ToggleLock, 18);
            UiBuilder.Place(lockButton, new Vector2(0.5f, 0), Vector2.zero, new Vector2(SideWidth, 44), new Vector2(0.5f, 0));
            lockImage = lockButton.GetComponent<Image>();
            lockText = lockButton.GetComponentInChildren<TMP_Text>();
        }

        Card BuildCard(UiBuilder ui, int slot) {
            var card = new Card { Slot = slot };
            card.Background = UiBuilder.Image(Root, $"Card{slot}", UiBuilder.RowColor);
            card.Rect = card.Background.rectTransform;
            UiBuilder.Place(card.Rect, new Vector2(0, 0.5f), new Vector2(12 + slot * (CardWidth + Gap), 0), new Vector2(CardWidth, CardHeight), new Vector2(0, 0.5f));
            card.Button = card.Background.gameObject.AddComponent<Button>();
            card.Button.targetGraphic = card.Background;
            card.Button.onClick.AddListener(() => {
                if (card.UnitId != null) game.Submit(new BuyUnitCommand { ShopSlot = card.Slot });
            });

            var frame = UiBuilder.Rect(card.Rect, "Picture");
            UiBuilder.Place(frame, new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(CardHeight - 8, CardHeight - 8), new Vector2(0, 0.5f));
            frame.gameObject.AddComponent<RectMask2D>();
            card.Thumbnail = UiBuilder.Icon(frame, "Thumbnail", null);

            var left = CardHeight + 2;
            card.Name = ui.Text(card.Rect, "", 18, TextAlignmentOptions.TopLeft, Color.white);
            UiBuilder.Place(card.Name.rectTransform, new Vector2(0, 1), new Vector2(left, -6), new Vector2(CardWidth - left - 6, 26), new Vector2(0, 1));
            card.Traits = ui.Text(card.Rect, "", 14, TextAlignmentOptions.BottomLeft, UiBuilder.MutedColor);
            card.Traits.enableWordWrapping = true;
            UiBuilder.Place(card.Traits.rectTransform, new Vector2(0, 0), new Vector2(left, 30), new Vector2(CardWidth - left - 6, 56), new Vector2(0, 0));
            card.Cost = ui.Text(card.Rect, "", 18, TextAlignmentOptions.BottomLeft, UiBuilder.GoldColor);
            UiBuilder.Place(card.Cost.rectTransform, new Vector2(0, 0), new Vector2(left, 6), new Vector2(CardWidth - left - 6, 24), new Vector2(0, 0));

            var sold = UiBuilder.Image(card.Rect, "Sold", new Color(0.03f, 0.04f, 0.07f, 0.8f));
            sold.raycastTarget = false;
            UiBuilder.Stretch(sold.rectTransform);
            var soldText = ui.Text(sold.rectTransform, "Đã mua", 20, TextAlignmentOptions.Center, UiBuilder.MutedColor);
            UiBuilder.Stretch(soldText.rectTransform);
            card.Sold = sold.gameObject;
            return card;
        }

        void ToggleLock() {
            var p = game.Session.LocalPlayer;
            if (p != null) game.Submit(new SetShopLockCommand { Locked = !p.Shop.Locked });
        }

        public void Refresh() {
            var p = game.Session.LocalPlayer;
            if (p == null) return;
            var rules = game.Db.Rules;
            var slots = p.Shop.Slots;
            var key = new StringBuilder();
            foreach (var s in slots) key.Append(s ?? "-").Append('|');
            key.Append(p.Level);
            if (key.ToString() != shown) {
                shown = key.ToString();
                for (var i = 0; i < cards.Count; i++) Fill(cards[i], i < slots.Length ? slots[i] : null);
                var odds = rules.Level(p.Level).ShopOdds;
                oddsText.text = $"<color=#C8D0DC>{odds[0]}%</color>  <color=#B98CFF>{odds[1]}%</color>  <color=#F2C84B>{odds[2]}%</color>";
            }
            refreshText.text = $"Làm mới ({rules.RefreshCost})";
            lockText.text = p.Shop.Locked ? "Đã khoá" : "Khoá";
            lockImage.color = p.Shop.Locked ? UiBuilder.GoldColor * 0.8f : UiBuilder.ButtonColor;
            foreach (var card in cards) {
                if (card.UnitId == null) continue;
                var price = rules.PriceOf(game.Db.Unit(card.UnitId).Reputation);
                card.Cost.color = p.Gold >= price ? UiBuilder.GoldColor : UiBuilder.BadColor;
            }
        }

        void Fill(Card card, string unitId) {
            card.UnitId = unitId;
            var trait = unitId != null ? HeroTraitDB.Instance.Find(unitId) : null;
            card.Sold.SetActive(trait == null);
            card.Button.interactable = trait != null;
            if (trait == null) {
                card.Thumbnail.enabled = false;
                card.Name.text = "";
                card.Cost.text = "";
                card.Traits.text = "";
                card.Background.color = UiBuilder.RowColor;
                return;
            }
            card.Thumbnail.enabled = true;
            card.Thumbnail.sprite = trait.thumbnail;
            UiBuilder.AspectFill(card.Thumbnail, (RectTransform)card.Thumbnail.transform.parent, new Vector2(0, -0.3f));
            card.Name.text = trait.name;
            card.Cost.text = $"{game.Db.Rules.PriceOf(trait.reputation)} vàng";
            var names = new List<string> { trait.realm.ToName() };
            var n = trait.role.GetRoles(RoleBuffer);
            for (var i = 0; i < n; i++) names.Add(RoleBuffer[i].ToName());
            card.Traits.text = string.Join(", ", names);
            card.Background.color = TierColor(trait.reputation);
        }

        /// <summary>Card tint by unit tier (Unknown / Elite / Legendary).</summary>
        public static Color TierColor(Reputation reputation) => reputation switch {
            Reputation.Elite => new Color(0.27f, 0.17f, 0.42f, 0.95f),
            Reputation.Legendary => new Color(0.45f, 0.33f, 0.1f, 0.95f),
            _ => new Color(0.16f, 0.2f, 0.27f, 0.95f),
        };
    }
}
