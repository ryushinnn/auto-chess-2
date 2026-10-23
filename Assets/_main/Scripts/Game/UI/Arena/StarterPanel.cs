using System.Collections.Generic;
using AutoChess.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>The starter pick of the setup phase: offers of a cheap unit + a component, each rerollable, then confirm.</summary>
    public sealed class StarterPanel {
        const float CardWidth = 320;
        const float CardHeight = 430;

        public sealed class Offer {
            public int Index;
            public RectTransform Rect;
            public Image Outline;
            public Image Picture;
            public TMP_Text Name;
            public TMP_Text Traits;
            public Image Item;
            public string ItemId;
        }

        static readonly Role[] RoleBuffer = new Role[6];

        readonly GameManager game;
        readonly RectTransform root;
        readonly List<Offer> offers = new();
        readonly Button confirm;

        /// <summary>Selected offer index, or -1.</summary>
        public int Selected { get; private set; } = -1;
        public bool IsOpen => root.gameObject.activeSelf;
        public IReadOnlyList<Offer> Offers => offers;

        public StarterPanel(GameManager game, UiBuilder ui, RectTransform parent) {
            this.game = game;
            var dim = UiBuilder.Image(parent, "Starter", new Color(0, 0, 0, 0.55f));
            root = dim.rectTransform;
            UiBuilder.Stretch(root);
            var count = game.Db.Rules.StarterOfferCount;
            var width = count * CardWidth + (count - 1) * 24 + 48;
            var panel = ui.Panel(root, "Panel", new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(width, CardHeight + 200), new Vector2(0.5f, 0.5f));
            var title = ui.Text(panel, "Chọn tướng và trang bị khởi đầu", 34, TextAlignmentOptions.Center, Color.white);
            UiBuilder.Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(width - 40, 46), new Vector2(0.5f, 1));
            for (var i = 0; i < count; i++) offers.Add(BuildOffer(ui, panel, i, count));
            var button = ui.Button(panel, "Xác nhận", Confirm, 26);
            button.gameObject.name = "Confirm";
            UiBuilder.Place(button, new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(260, 56), new Vector2(0.5f, 0));
            confirm = button.GetComponent<Button>();
            root.gameObject.SetActive(false);
        }

        Offer BuildOffer(UiBuilder ui, RectTransform panel, int index, int count) {
            var offer = new Offer { Index = index };
            offer.Outline = UiBuilder.Image(panel, $"Offer{index}", Color.clear);
            offer.Rect = offer.Outline.rectTransform;
            var x = (index - (count - 1) / 2f) * (CardWidth + 24);
            UiBuilder.Place(offer.Rect, new Vector2(0.5f, 1), new Vector2(x, -80), new Vector2(CardWidth, CardHeight), new Vector2(0.5f, 1));
            var card = UiBuilder.Image(offer.Rect, "Card", UiBuilder.RowColor);
            UiBuilder.Stretch(card.rectTransform, 3, 3, 3, 3);
            var select = card.gameObject.AddComponent<Button>();
            select.targetGraphic = card;
            select.onClick.AddListener(() => Select(index));

            var frame = UiBuilder.Rect(card.rectTransform, "Picture");
            UiBuilder.Place(frame, new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(CardWidth - 22, 270), new Vector2(0.5f, 1));
            frame.gameObject.AddComponent<RectMask2D>();
            offer.Picture = UiBuilder.Icon(frame, "Thumbnail", null);

            offer.Name = ui.Text(card.rectTransform, "", 28, TextAlignmentOptions.Left, Color.white);
            UiBuilder.Place(offer.Name.rectTransform, new Vector2(0, 0), new Vector2(14, 92), new Vector2(CardWidth - 110, 36), new Vector2(0, 0));
            offer.Traits = ui.Text(card.rectTransform, "", 16, TextAlignmentOptions.Left, UiBuilder.MutedColor);
            UiBuilder.Place(offer.Traits.rectTransform, new Vector2(0, 0), new Vector2(14, 66), new Vector2(CardWidth - 110, 24), new Vector2(0, 0));

            var itemBack = UiBuilder.Image(card.rectTransform, "Item", UiBuilder.FieldColor);
            UiBuilder.Place(itemBack.rectTransform, new Vector2(1, 0), new Vector2(-12, 62), new Vector2(72, 72), new Vector2(1, 0));
            offer.Item = UiBuilder.Icon(itemBack.rectTransform, "Icon", null);
            UiBuilder.Stretch(offer.Item.rectTransform, 4, 4, 4, 4);
            Hover.On(itemBack, () => {
                if (offer.ItemId != null) Tooltip.ShowItem(offer.ItemId, itemBack.rectTransform);
            });

            var reroll = ui.Button(card.rectTransform, "Đổi lựa chọn", () => Reroll(index), 18);
            reroll.gameObject.name = "Reroll";
            UiBuilder.Place(reroll, new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(CardWidth - 26, 42), new Vector2(0.5f, 0));
            return offer;
        }

        public void SetOpen(bool open) {
            root.gameObject.SetActive(open);
            if (open) Refresh();
        }

        public void Reset() {
            Selected = -1;
            Paint();
        }

        public void Refresh() {
            var p = game.Session.LocalPlayer;
            if (p == null) return;
            for (var i = 0; i < offers.Count && i < p.StarterOffers.Length; i++) {
                var data = p.StarterOffers[i];
                var offer = offers[i];
                var hero = data.UnitId != null ? HeroTraitDB.Instance.Find(data.UnitId) : null;
                var item = data.ItemId != null ? ItemDB.Instance.Find(data.ItemId) : null;
                offer.Picture.enabled = hero != null;
                if (hero != null) {
                    offer.Picture.sprite = hero.thumbnail;
                    UiBuilder.AspectFill(offer.Picture, (RectTransform)offer.Picture.transform.parent, new Vector2(0, -0.2f));
                    offer.Name.text = hero.name;
                    var names = new List<string> { hero.realm.ToName() };
                    var n = hero.role.GetRoles(RoleBuffer);
                    for (var r = 0; r < n; r++) names.Add(RoleBuffer[r].ToName());
                    offer.Traits.text = string.Join(", ", names);
                }
                offer.ItemId = item != null ? data.ItemId : null;
                offer.Item.enabled = item != null;
                if (item != null) offer.Item.sprite = item.icon;
            }
            Paint();
        }

        void Paint() {
            foreach (var offer in offers) offer.Outline.color = offer.Index == Selected ? UiBuilder.GoldColor : Color.clear;
            if (confirm != null) confirm.interactable = Selected >= 0;
        }

        public void Select(int index) {
            Selected = index;
            Paint();
        }

        void Reroll(int index) {
            if (Selected == index) Selected = -1;
            Paint();
            game.Submit(new RerollStarterCommand { OfferIndex = index });
        }

        void Confirm() {
            if (Selected < 0) return;
            // on success the match leaves the setup phase and ArenaScreen swaps to the HUD
            game.Submit(new ChooseStarterCommand { OfferIndex = Selected });
        }
    }
}
