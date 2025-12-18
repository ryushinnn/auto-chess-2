using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InventoryUI : BaseUI {
    [SerializeField] Inventory_Item itemPrefab;
    [SerializeField] Transform itemContainer;
    [SerializeField] Button leftButton;
    [SerializeField] Button rightButton;
    [SerializeField] TMP_Text pageText;
    [SerializeField] Image pickingItem;

    List<Inventory_Item> items;
    int currentPage;
    int maxPage;
    
    const int ITEM_PER_PAGE = 8;

    void Awake() {
        leftButton.onClick.AddListener(NavigateLeft);
        rightButton.onClick.AddListener(NavigateRight);
        pickingItem.gameObject.SetActive(false);
    }

    public void SetData(Dictionary<Item, int> availableItems) {
        items ??= itemContainer.GetComponentsInChildren<Inventory_Item>().ToList();

        var index = 0;
        foreach (var (i, qty) in availableItems) {
            if (index >= items.Count) {
                var item = Instantiate(itemPrefab, itemContainer);
                items.Add(item);
            }
            
            items[index].gameObject.SetActive(true);
            items[index].SetData(i.icon, qty,
                () => {
                    pickingItem.gameObject.SetActive(true);
                    pickingItem.sprite = i.icon;
                },
                (pos) => {
                    pickingItem.transform.position = pos;
                }, () => {
                    pickingItem.gameObject.SetActive(false);
                    GameManager.Instance.Inventory.EquipItem(i);
                });
            index++;
        }

        for (int i = index; i < items.Count; i++) {
            items[i].gameObject.SetActive(false);
            items[i].MarkAsEmpty();
        }

        maxPage = (index + ITEM_PER_PAGE - 1) / ITEM_PER_PAGE - 1;
        currentPage = Mathf.Clamp(currentPage, 0, maxPage);
        RefreshCurrentPage();
    }
    
    void NavigateLeft() {
        currentPage--;
        if (currentPage < 0) currentPage = maxPage;
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
        pageText.text = $"{currentPage+1}/{maxPage+1}";
    }
}