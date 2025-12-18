using System;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(menuName = "Item")]
public class Item : ScriptableObject {
    public string name;
    [PreviewField] public Sprite icon;
    public Item[] ingredients;
    public AttributeModifier[] modifiers;

    public bool IsForgedItem() {
        return ingredients != null && ingredients.Length == 2;
    }
}