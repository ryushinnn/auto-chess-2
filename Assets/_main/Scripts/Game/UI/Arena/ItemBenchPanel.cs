using System.Collections.Generic;
using System.Linq;
using System.Text;
using AutoChess.Core;
using RExt.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>
    /// The item bench (left). Dragging an item onto an owned unit equips it (the drop is resolved from the pointer
    /// position, so the AutoTest drag driver can perform it too). Holding a component over another one (or over its own
    /// stack when there are two) previews the forged item with a bar that fills in <see cref="ForgeSeconds"/>; releasing
    /// once it is full forges them on the bench.
    /// </summary>
    public sealed class ItemBenchPanel {
        public const float ForgeSeconds = 2f;
        const int Columns = 5;
        const float Slot = 44;
        const float Gap = 6;
        const float Header = 34;
        const float Width = Columns * Slot + (Columns - 1) * Gap + 16;

        readonly GameManager game;
        readonly UiBuilder ui;
        readonly RectTransform canvas;
        readonly List<ItemSlot> slots = new();
        readonly Image ghost;
        readonly TMP_Text title;
        string shown;

        ItemSlot dragged;
        ItemSlot target;
        float targetSince;

        public RectTransform Root { get; }
        public IEnumerable<ItemSlot> Slots => slots.Where(s => s.gameObject.activeSelf && s.ItemId != null);

        public ItemBenchPanel(GameManager game, UiBuilder ui, RectTransform parent, RectTransform canvas) {
            this.game = game;
            this.ui = ui;
            this.canvas = canvas;
            Root = ui.Panel(parent, "Items", new Vector2(0, 0), new Vector2(12, 160), new Vector2(Width, Header));
            title = ui.Text(Root, "Trang bị", 18, TextAlignmentOptions.Left, UiBuilder.MutedColor);
            UiBuilder.Place(title.rectTransform, new Vector2(0, 1), new Vector2(12, -4), new Vector2(Width - 24, 26), new Vector2(0, 1));
            ghost = UiBuilder.Icon(canvas, "DraggedItem", null);
            ghost.rectTransform.sizeDelta = new Vector2(Slot, Slot);
            ghost.color = new Color(1, 1, 1, 0.85f);
            ghost.gameObject.SetActive(false);
        }

        public void Refresh() {
            if (dragged != null) return; // never rebuild under the pointer
            var p = game.Session.LocalPlayer;
            if (p == null) return;
            var key = new StringBuilder();
            foreach (var s in p.Items) key.Append(s.ItemId).Append(':').Append(s.Count).Append(',');
            if (key.ToString() == shown) return;
            shown = key.ToString();

            var index = 0;
            foreach (var stack in p.Items) {
                var item = ItemDB.Instance.Find(stack.ItemId);
                if (item == null) continue;
                if (index >= slots.Count) slots.Add(BuildSlot(index));
                slots[index].Set(stack.ItemId, item.icon, stack.Count);
                index++;
            }
            for (var i = 0; i < slots.Count; i++) {
                if (i >= index) slots[i].Set(null, null, 0);
                slots[i].gameObject.SetActive(i < index);
            }
            var rowsUsed = Mathf.Max(1, (index + Columns - 1) / Columns);
            title.text = index == 0 ? "Trang bị - trống" : "Trang bị";
            Root.sizeDelta = new Vector2(Width, Header + (index == 0 ? 0 : rowsUsed * (Slot + Gap) + 4));
            for (var i = 0; i < index; i++) {
                var rect = (RectTransform)slots[i].transform;
                rect.anchoredPosition = new Vector2(8 + i % Columns * (Slot + Gap), -Header - i / Columns * (Slot + Gap));
            }
        }

        ItemSlot BuildSlot(int index) {
            var background = UiBuilder.Image(Root, $"Item{index}", UiBuilder.RowColor);
            UiBuilder.Place(background.rectTransform, new Vector2(0, 1), Vector2.zero, new Vector2(Slot, Slot), new Vector2(0, 1));
            var slot = background.gameObject.AddComponent<ItemSlot>();
            slot.Build(ui, this);
            return slot;
        }

        /// <summary>Called every frame: grows the forge bar while an item is held over a partner.</summary>
        public void Tick() {
            if (dragged == null || target == null) return;
            var progress = (Time.unscaledTime - targetSince) / ForgeSeconds;
            Tooltip.SetProgress(progress);
            target.SetForgeProgress(progress);
        }

        // ------------------------------------------------------------------ dragging

        internal void BeginDrag(ItemSlot slot) {
            Tooltip.Hide();
            dragged = slot;
            ghost.sprite = slot.Icon.sprite;
            ghost.gameObject.SetActive(true);
            ghost.transform.SetAsLastSibling();
            target = null;
        }

        internal void Drag(PointerEventData data) {
            if (dragged == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, data.position, null, out var local);
            ghost.rectTransform.anchoredPosition = local;
            ghost.rectTransform.anchorMin = ghost.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            var under = UiUnder(data);
            var over = under != null ? under.GetComponentInParent<ItemSlot>() : null;
            if (over != null && !CanForge(dragged, over)) over = null;
            if (over == target) return;
            if (target != null) target.SetForgeProgress(-1);
            target = over;
            targetSince = Time.unscaledTime;
            if (target == null) {
                Tooltip.Hide();
                return;
            }
            var result = game.Db.Combine(dragged.ItemId, target.ItemId);
            Tooltip.ShowItem(result.Id, (RectTransform)target.transform);
            Tooltip.SetProgress(0);
        }

        internal void EndDrag(PointerEventData data) {
            if (dragged == null) return;
            var item = dragged.ItemId;
            var partner = target;
            var ready = partner != null && Time.unscaledTime - targetSince >= ForgeSeconds;
            if (partner != null) partner.SetForgeProgress(-1);
            ghost.gameObject.SetActive(false);
            Tooltip.Hide();
            dragged = null;
            target = null;
            if (partner != null) {
                if (ready) game.Submit(new CombineItemsCommand { First = item, Second = partner.ItemId });
                return;
            }
            var under = UiUnder(data);
            if (under != null && under.transform.IsChildOf(canvas)) return; // dropped on the HUD
            TryEquip(item, data.position);
        }

        static readonly List<RaycastResult> Hits = new();

        /// <summary>The topmost UI element under the pointer (raycast here: synthetic drags carry no raycast).</summary>
        static GameObject UiUnder(PointerEventData data) {
            if (EventSystem.current == null) return null;
            Hits.Clear();
            EventSystem.current.RaycastAll(data, Hits);
            foreach (var hit in Hits) {
                if (hit.gameObject != null && hit.module is GraphicRaycaster) return hit.gameObject;
            }
            return null;
        }

        /// <summary>Two components with a recipe; the same stack only when it holds two copies.</summary>
        bool CanForge(ItemSlot from, ItemSlot to) {
            var db = game.Db;
            if (from.ItemId == null || to.ItemId == null) return false;
            if (!db.TryGetItem(from.ItemId, out var a) || !db.TryGetItem(to.ItemId, out var b) || a.IsCombined || b.IsCombined) return false;
            if (from == to && from.Count < 2) return false;
            return db.Combine(from.ItemId, to.ItemId) != null;
        }

        /// <summary>Equips <paramref name="itemId"/> on the owned unit under the screen point, if any.</summary>
        void TryEquip(string itemId, Vector2 screenPoint) {
            if (game.IsScouting) return;
            var ray = Utils.MainCamera().ScreenPointToRay(screenPoint);
            foreach (var hit in Physics.RaycastAll(ray, 100f).OrderBy(h => h.distance)) {
                var unit = hit.collider.GetComponentInParent<RosterUnitView>();
                if (unit == null) continue;
                game.Submit(new EquipItemCommand { ItemId = itemId, UnitId = unit.UnitId });
                return;
            }
        }
    }

    /// <summary>One stack of the item bench: icon, count, hover card, drag source and forge target.</summary>
    public sealed class ItemSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler {
        public string ItemId { get; private set; }
        public int Count { get; private set; }
        public Image Icon { get; private set; }

        ItemBenchPanel bench;
        TMP_Text count;
        Image forge;

        internal void Build(UiBuilder ui, ItemBenchPanel owner) {
            bench = owner;
            Icon = UiBuilder.Icon(transform, "Icon", null);
            UiBuilder.Stretch(Icon.rectTransform, 2, 2, 2, 2);
            forge = UiBuilder.Image(transform, "Forge", new Color(UiBuilder.GoodColor.r, UiBuilder.GoodColor.g, UiBuilder.GoodColor.b, 0.45f));
            forge.raycastTarget = false;
            forge.rectTransform.anchorMin = Vector2.zero;
            forge.rectTransform.anchorMax = new Vector2(1, 0);
            forge.rectTransform.offsetMin = forge.rectTransform.offsetMax = Vector2.zero;
            forge.gameObject.SetActive(false);
            count = ui.Text(transform, "", 16, TextAlignmentOptions.BottomRight, Color.white);
            count.fontStyle = FontStyles.Bold;
            count.outlineWidth = 0.25f;
            count.outlineColor = Color.black;
            UiBuilder.Stretch(count.rectTransform, 2, 3, 2, 1);
        }

        public void Set(string itemId, Sprite sprite, int amount) {
            ItemId = itemId;
            Count = amount;
            Icon.sprite = sprite;
            Icon.enabled = sprite != null;
            count.text = amount > 1 ? amount.ToString() : "";
        }

        /// <summary>Forge preview fill over the slot (0..1, from the bottom); negative hides it.</summary>
        public void SetForgeProgress(float value) {
            forge.gameObject.SetActive(value >= 0);
            if (value < 0) return;
            forge.rectTransform.anchorMax = new Vector2(1, Mathf.Clamp01(value));
            forge.color = value >= 1 ? new Color(0.35f, 0.85f, 0.4f, 0.6f) : new Color(0.35f, 0.75f, 1f, 0.45f);
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (ItemId != null && !eventData.dragging) Tooltip.ShowItem(ItemId, (RectTransform)transform);
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (!eventData.dragging) Tooltip.Hide();
        }

        void OnDisable() {
            if (forge != null) forge.gameObject.SetActive(false);
        }

        public void OnBeginDrag(PointerEventData eventData) {
            if (ItemId == null) return;
            Icon.color = new Color(1, 1, 1, 0.4f);
            bench.BeginDrag(this);
        }

        public void OnDrag(PointerEventData eventData) => bench.Drag(eventData);

        public void OnEndDrag(PointerEventData eventData) {
            Icon.color = Color.white;
            bench.EndDrag(eventData);
        }
    }
}
