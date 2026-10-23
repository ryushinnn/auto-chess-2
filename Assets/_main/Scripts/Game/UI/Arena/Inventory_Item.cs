using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AutoChess.Game {
    public class Inventory_Item : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler,
        IPointerExitHandler {
        [SerializeField] Image iconImage;
        [SerializeField] TMP_Text quantityText;

        public bool Empty => empty;
        public string ItemId { get; private set; }

        bool empty = true;
        bool showQuantity;
        Action onPick;
        Action<Vector2> onMove;
        Action<Vector2> onRelease;

        public void SetData(string itemId, Sprite icon, int quantity, Action picked, Action<Vector2> moved, Action<Vector2> released) {
            empty = false;
            ItemId = itemId;
            iconImage.sprite = icon;
            showQuantity = quantity > 1;
            quantityText.enabled = showQuantity;
            quantityText.text = quantity.ToString();
            onPick = picked;
            onMove = moved;
            onRelease = released;
        }

        public void MarkAsEmpty() {
            empty = true;
            ItemId = null;
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (!empty && !eventData.dragging) ItemTooltip.Show(ItemId, (RectTransform)transform);
        }

        public void OnPointerExit(PointerEventData eventData) => ItemTooltip.Hide();

        void OnDisable() => ItemTooltip.Hide();

        public void OnBeginDrag(PointerEventData eventData) {
            ItemTooltip.Hide();
            iconImage.color = new Color(1, 1, 1, 0.5f);
            quantityText.enabled = false;
            onPick?.Invoke();
        }

        public void OnDrag(PointerEventData eventData) {
            onMove?.Invoke(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData) {
            iconImage.color = Color.white;
            quantityText.enabled = showQuantity;
            onRelease?.Invoke(eventData.position);
        }
    }
}
