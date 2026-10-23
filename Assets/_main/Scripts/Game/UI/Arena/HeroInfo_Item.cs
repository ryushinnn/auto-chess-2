using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AutoChess.Game {
    public class HeroInfo_Item : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
        [SerializeField] Image image;

        string itemId;

        public void SetData(Item item) {
            itemId = item != null ? item.Id : null;
            image.enabled = item != null;
            if (item != null) image.sprite = item.icon;
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (itemId != null) ItemTooltip.Show(itemId, (RectTransform)transform);
        }

        public void OnPointerExit(PointerEventData eventData) => ItemTooltip.Hide();

        void OnDisable() => ItemTooltip.Hide();
    }
}
