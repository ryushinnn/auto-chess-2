using System;
using RExt.Extensions;
using Sirenix.OdinInspector;
using Unity.VisualScripting;
using UnityEngine;

public static class AttributeModifierKey {
    public const string MaxHp = "max_hp";
    public const string PhysicalDamage = "p_dmg";
    public const string MagicalDamage = "m_dmg";
    public const string Armor = "armor";
    public const string Resistance = "resistance";
    public const string AttackSpeed = "atk_spd";
    public const string CriticalChance = "crit_chance";
    public const string CriticalDamage = "crit_dmg";
    public const string EnergyRegenEfficient = "energy_regen_eff";
    public const string PhysicalPenetration = "p_pen";
    public const string MagicalPenetration = "m_pen";
    public const string LifeSteal = "life_steal";
    public const string Tenacity = "tenacity";
}

[Serializable]
public class AttributeModifier {
    public enum Type {
        FixedValue,
        Percentage
    }
    
    const string ID_PLACE_HOLDER = "<will be auto generated>";
    
    [HideIf("@this.id == AttributeModifier.ID_PLACE_HOLDER")] 
    public string id = ID_PLACE_HOLDER;
    [StringDropdown(typeof(AttributeModifierKey))] 
    public string key;
    public float value;
    public Type type;

    public static AttributeModifier Create(string key, float value, Type type) {
        return new AttributeModifier {
            key = key,
            id = Guid.NewGuid().ToString(),
            value = value,
            type = type
        };
    }
}

[Serializable]
public class AttributeModifierSet {
    [TableColumnWidth(150, resizable:false)] public string effectKey;
    [VerticalGroup("Effect")] public string id;
    [VerticalGroup("Effect")] public Hero owner;
    [VerticalGroup("Effect")] public AttributeModifier[] modifiers;
    [VerticalGroup("Effect")] public int stacks;
    [VerticalGroup("Effect")] public bool permanent;
    [VerticalGroup("Effect"), HideIf("permanent")] public float duration;
    [HideInInspector] public Mark mark;
    public Action onRemove;

    public static AttributeModifierSet Create(Hero owner, string effectKey, float duration, (string key, float value, AttributeModifier.Type type)[] modifiers, int stacks = 1, bool createMark = true, Action onRemove = null) {
        var set = new AttributeModifierSet {
            effectKey = effectKey,
            id = Guid.NewGuid().ToString(),
            owner = owner,
            modifiers = new AttributeModifier[modifiers.Length],
            stacks = stacks,
            duration = duration,
            permanent = false,
            mark = createMark
                ? Mark.Create(
                    owner,
                    effectKey,
                    stacks,
                    duration
                )
                : null,
            onRemove = onRemove
        };

        for (int i = 0; i < modifiers.Length; i++) {
            set.modifiers[i] = AttributeModifier.Create(
                modifiers[i].key,
                modifiers[i].value,
                modifiers[i].type
            );
        }

        return set;
    }
    
    public static AttributeModifierSet Create(Hero owner, string effectKey, (string key, float value, AttributeModifier.Type type)[] modifiers, int stacks = 1, bool createMark = true, Action onRemove = null) {
        var set = new AttributeModifierSet {
            effectKey = effectKey,
            id = Guid.NewGuid().ToString(),
            owner = owner,
            modifiers = new AttributeModifier[modifiers.Length],
            stacks = stacks,
            duration = Mathf.Infinity,
            permanent = true,
            mark = createMark
                ? Mark.Create(
                    owner,
                    effectKey,
                    stacks
                )
                : null,
            onRemove = onRemove
        };

        for (int i = 0; i < modifiers.Length; i++) {
            set.modifiers[i] = AttributeModifier.Create(
                modifiers[i].key,
                modifiers[i].value,
                modifiers[i].type
            );
        }

        return set;
    }

    public bool SameAs(AttributeModifierSet other) {
        return effectKey == other.effectKey && owner == other.owner;
    }
}