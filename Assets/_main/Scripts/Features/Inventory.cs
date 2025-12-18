using System.Collections.Generic;
using RExt.Utils;
using Sirenix.OdinInspector;
using UnityEngine;

public class Inventory : MonoBehaviour {
    public int Coins => coins;
    
    [SerializeField] LayerMask heroLayerMask;

    [SerializeField] private Item[] hackItems;
    
    Dictionary<Item, int> items = new();
    int coins;

    public void Initialize() {
        coins = 0;
        UIManager_Arena.Instance.Arena.UpdateCoinsText(coins);
        UIManager_Arena.Instance.Inventory.SetData(items);
    }
    
    [Button]
    public void AddItem(Item item) {
        if (items.ContainsKey(item)) {
            items[item]++;
        }
        else {
            items[item] = 1;
        }
        UIManager_Arena.Instance.Destinies.Close();
        UIManager_Arena.Instance.Inventory.Open();
        UIManager_Arena.Instance.Inventory.SetData(items);
    }

    [Button]
    public void AddHackItems()
    {
        foreach (var item in hackItems)
        {
            for (int i = 0; i < 3; i++)
            {
                AddItem(item);
            }
        }
    }

    [Button]
    public void EquipItem(Item item) {
        if (!items.ContainsKey(item)) return;
        
        var ray = Utils.MainCamera().ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 100f, heroLayerMask)) {
            if (hit.collider.TryGetComponent<LineUpHero>(out var hero)) {
                if (hero.GetAbility<HeroInventory>().Add(item)) {
                    items[item]--;
                    if (items[item] == 0) {
                        items.Remove(item);
                    }
                    UIManager_Arena.Instance.Inventory.SetData(items);
                }
            }
        }
    }

    public void GainCoins(int amount) {
        coins += amount;
        UIManager_Arena.Instance.Arena.UpdateCoinsText(coins);
    }
    
    public void SpendCoins(int amount) {
        coins -= amount;
        UIManager_Arena.Instance.Arena.UpdateCoinsText(coins);
    }

    [Button]
    void dev_addCoins() {
        GainCoins(1000);
    }
}