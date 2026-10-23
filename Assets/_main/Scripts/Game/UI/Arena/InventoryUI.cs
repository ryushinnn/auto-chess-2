using System.Collections.Generic;
using System.Linq;
using AutoChess.Core;
using RExt.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>
    /// The item bench, paged. Dragging an item onto an owned unit submits an EquipItemCommand (the drop is
    /// resolved from the pointer position of the drag, so the AutoTest drag driver can perform it too).
    /// </summary>
    public class InventoryUI : BaseUI, ISessionView, ISessionResettable {
        [SerializeField] Inventory_Item itemPrefab;
        [SerializeField] Transform itemContainer;
        [SerializeField] Button leftButton;
        [SerializeField] Button rightButton;
        [SerializeField] TMP_Text pageText;
        [SerializeField] Image pickingItem;

        const int ITEM_PER_PAGE = 8;

        List<Inventory_Item> items;
        int currentPage;
        int maxPage;
        GameManager game;

        void Awake() {
            leftButton.onClick.AddListener(NavigateLeft);
            rightButton.onClick.AddListener(NavigateRight);
            pickingItem.gameObject.SetActive(false);
        }

        public void Bind(GameManager gameManager) {
            game = gameManager;
            game.EventRaised += OnEvent;
            Refresh();
        }

        void OnDestroy() {
            if (game != null) game.EventRaised -= OnEvent;
        }

        void OnEvent(GameEvent e) {
            if (!(e is ItemBenchChangedEvent changed) || changed.PlayerId != game.Session.LocalPlayerId) return;
            if (changed.AddedItemId != null) {
                // a new item shows the item bench (like the old Inventory.AddItem)
                UIManager_Arena.Instance.Destinies.Close();
                Open();
            }
            Refresh();
        }

        public void OnSessionReset() {
            currentPage = 0;
            pickingItem.gameObject.SetActive(false);
            Refresh();
        }

        void Refresh() {
            SetData(game.Session.LocalPlayer.Items);
        }

        public void SetData(IReadOnlyList<ItemStack> stacks) {
            items ??= itemContainer.GetComponentsInChildren<Inventory_Item>(true).ToList();

            var index = 0;
            foreach (var stack in stacks) {
                var item = ItemDB.Instance.Find(stack.ItemId);
                if (item == null) continue;
                if (index >= items.Count) items.Add(Instantiate(itemPrefab, itemContainer));

                var itemId = stack.ItemId;
                items[index].gameObject.SetActive(true);
                items[index].SetData(itemId, item.icon, stack.Count,
                    () => {
                        pickingItem.gameObject.SetActive(true);
                        pickingItem.sprite = item.icon;
                    },
                    pos => pickingItem.transform.position = pos,
                    pos => {
                        pickingItem.gameObject.SetActive(false);
                        TryEquip(itemId, pos);
                    });
                index++;
            }

            for (int i = index; i < items.Count; i++) {
                items[i].gameObject.SetActive(false);
                items[i].MarkAsEmpty();
            }

            maxPage = (index + ITEM_PER_PAGE - 1) / ITEM_PER_PAGE - 1;
            currentPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, maxPage));
            RefreshCurrentPage();
        }

        /// <summary>Equips <paramref name="itemId"/> on the owned unit under the screen point, if any.</summary>
        void TryEquip(string itemId, Vector2 screenPoint) {
            var ray = Utils.MainCamera().ScreenPointToRay(screenPoint);
            foreach (var hit in Physics.RaycastAll(ray, 100f).OrderBy(h => h.distance)) {
                var unit = hit.collider.GetComponentInParent<RosterUnitView>();
                if (unit == null) continue;
                game.Submit(new EquipItemCommand { ItemId = itemId, UnitId = unit.UnitId });
                return;
            }
        }

        void NavigateLeft() {
            currentPage--;
            if (currentPage < 0) currentPage = Mathf.Max(0, maxPage);
            RefreshCurrentPage();
        }

        void NavigateRight() {
            currentPage++;
            if (currentPage > maxPage) currentPage = 0;
            RefreshCurrentPage();
        }

        void RefreshCurrentPage() {
            var index = 0;
            foreach (var i in items) {
                if (i.Empty) continue;
                i.gameObject.SetActive(index / ITEM_PER_PAGE == currentPage);
                index++;
            }
            pageText.text = $"{currentPage + 1}/{Mathf.Max(0, maxPage) + 1}";
        }
    }
}
