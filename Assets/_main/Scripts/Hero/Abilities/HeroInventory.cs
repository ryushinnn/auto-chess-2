using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

public class HeroInventory : HeroAbility {
    HeroAttributes attributes;
    
    [SerializeField] Image[] itemIcons;
    [SerializeField] RectTransform ui;
    [SerializeField, ReadOnly] List<ItemSlot> itemSlots = new();
    
    const int CAPACITY = 3;
    const float UI_HEIGHT_NO_ITEM = 2.5f;
    const float UI_HEIGHT_WITH_ITEM = 3.25f;

    public override void Initialize(Hero hero) {
        base.Initialize(hero);
        foreach (var i in itemIcons) {
            i.enabled = false;
        }
    }

    public override void ResetAll() {
        itemSlots.Clear();
        foreach (var icon in itemIcons) {
            icon.enabled = false;
        }
        ui.anchoredPosition = new Vector2(ui.anchoredPosition.x, UI_HEIGHT_NO_ITEM);
    }

    protected override void FindReferences() {
        attributes = hero.GetAbility<HeroAttributes>();
    }

    public bool Add(Item item) {
        if (itemSlots.Count == CAPACITY && (item.IsForgedItem() || itemSlots[^1].item.IsForgedItem())) {
            return false;
        }

        ItemSlot slot;
        if (item.IsForgedItem() || itemSlots.Count == 0 || itemSlots[^1].item.IsForgedItem()) {
            slot = new ItemSlot(item);
        }
        else {
            var completeItem = ItemDB.Instance.FindForgedItem(item, itemSlots[^1].item);
            itemSlots.RemoveAt(itemSlots.Count - 1);
            slot = new ItemSlot(completeItem);
        }

        var modifiers = new (string, float, AttributeModifier.Type)[slot.item.modifiers.Length];
        for (int i = 0; i < slot.item.modifiers.Length; i++) {
            var m = slot.item.modifiers[i];
            modifiers[i] = (m.key, m.value, m.type);
        }

        var modifierSet = AttributeModifierSet.Create(
            hero, 
            $"[ITEM_#{itemSlots.Count}]", 
            modifiers,
            createMark: false);

        attributes.AddAttributeModifier(modifierSet);
        slot.modifierSet = modifierSet;
        
        itemSlots.Add(slot);
        itemSlots.Sort((a, b) => {
            if (a.item.IsForgedItem() && !b.item.IsForgedItem()) return -1;
            if (!a.item.IsForgedItem() && b.item.IsForgedItem()) return 1;
            return 0;
        });
        for (int i = 0; i < itemIcons.Length; i++) {
            if (i >= itemSlots.Count) {
                itemIcons[i].enabled = false;
                continue;
            }
            
            itemIcons[i].enabled = true;
            itemIcons[i].sprite = itemSlots[i].item.icon;
        }
        ui.anchoredPosition = new Vector2(ui.anchoredPosition.x, itemSlots.Count > 0 ? UI_HEIGHT_WITH_ITEM : UI_HEIGHT_NO_ITEM);
        return true;
    }

    public Item[] Get() {
        return itemSlots.Select(x => x.item).ToArray();
    }

    [Button]
    void Dev_Add(Item item) {
        Add(item);
    }

    [Button]
    void Dev_RemoveAll() {
        foreach (var s in itemSlots) {
            // attributes.RemoveAttributeModifier(s.modifierSet); //later
        }
        itemSlots.Clear();
    }
}

[Serializable]
public class ItemSlot {
    public Item item;
    public AttributeModifierSet modifierSet;
    
    public ItemSlot(Item item) {
        this.item = item;
    }
}