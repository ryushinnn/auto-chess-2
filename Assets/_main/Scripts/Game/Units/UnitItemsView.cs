using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>Item icons above a unit (the visual half of the old HeroInventory).</summary>
    public class UnitItemsView : UnitPart {
        [SerializeField] Image[] itemIcons;
        [SerializeField] RectTransform ui;

        const float UI_HEIGHT_NO_ITEM = 2.5f;
        const float UI_HEIGHT_WITH_ITEM = 3.25f;

        public override void Initialize(UnitView owner) {
            base.Initialize(owner);
            foreach (var icon in itemIcons) icon.enabled = false;
        }

        public override void ResetAll(bool home) {
            SetItems(null);
        }

        public void SetItems(IReadOnlyList<string> itemIds) {
            var count = itemIds?.Count ?? 0;
            for (var i = 0; i < itemIcons.Length; i++) {
                var item = i < count ? ItemDB.Instance.Find(itemIds[i]) : null;
                itemIcons[i].enabled = item != null;
                if (item != null) itemIcons[i].sprite = item.icon;
            }
            ui.anchoredPosition = new Vector2(ui.anchoredPosition.x, count > 0 ? UI_HEIGHT_WITH_ITEM : UI_HEIGHT_NO_ITEM);
        }
    }
}
