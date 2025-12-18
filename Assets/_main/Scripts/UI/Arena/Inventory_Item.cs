using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class Inventory_Item : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler {
    
    [SerializeField] Image iconImage;
    [SerializeField] TMP_Text quantityText;

    public bool Empty => empty;
    
    bool empty;
    bool showQuantity;
    Action onPick;
    Action<Vector2> onMove;
    Action onRelease;

    public void SetData(Sprite icon, int quantity, Action onPick, Action<Vector2> onMove, Action onRelease) {
        empty = false;
        iconImage.sprite = icon;
        showQuantity = quantity > 1;
        quantityText.enabled = showQuantity;
        quantityText.text = quantity.ToString();
        this.onPick = onPick;
        this.onMove = onMove;
        this.onRelease = onRelease;
    }

    public void MarkAsEmpty() {
        empty = true;
    }

    public void OnBeginDrag(PointerEventData eventData) {
        iconImage.color = new Color(1,1,1,0.5f);
        quantityText.enabled = false;
        onPick?.Invoke();
    }

    public void OnDrag(PointerEventData eventData) {
        onMove?.Invoke(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData) {
        iconImage.color = Color.white;
        quantityText.enabled = showQuantity;
        onRelease?.Invoke();
    }
}