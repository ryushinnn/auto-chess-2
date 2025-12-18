using System;
using Sirenix.OdinInspector;
using UnityEngine;

public abstract class HeroAbility : MonoBehaviour {
    [SerializeField, ReadOnly] protected bool isActive = true;

    public bool IsActive => isActive;

    [NonSerialized] protected Hero hero;
    
    public virtual void Initialize(Hero hero) {
        this.hero = hero;
        FindReferences();
    }

    public void Disable() {
        isActive = false;
        name = $"[DISABLED] {name}";
    }

    public virtual void ResetAll() { }
    public virtual void Process() { }
    
    protected virtual void FindReferences() { }
}