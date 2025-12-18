using System;
using System.Collections.Generic;
using RExt.Extensions;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

public enum HeroRank {
    B,
    A,
    S
}

public class Hero : MonoBehaviour {
    public Vector3 WorldPosition {
        get => transform.position;
        set => transform.position = value.ToZeroY();
    }
    
    [SerializeField] protected Transform model;
    [SerializeField] protected Transform abilitiesContainer;
    [SerializeField] protected Canvas canvas;
    [SerializeField] protected Image rankIcon;

    public HeroTrait Trait => trait;
    public HeroRank Rank => rank;
    public Transform Model => model;
    public Mecanim Mecanim => mecanim;
    
    [SerializeField, ReadOnly] protected HeroTrait trait;
    [SerializeField, ReadOnly] protected HeroRank rank;
    
    protected Mecanim mecanim;
    protected List<HeroAbility> abilities = new();
    protected Dictionary<Type, HeroAbility> cachedAbilities = new();

    void Awake() {
        Initialize();
    }

    void Update() {
        Process();
    }
    
    protected virtual void Initialize() {
        FindAbilities();
        abilities.ForEach(x=>x.Initialize(this));
    }

    public virtual void Activate() {
        gameObject.SetActive(true);
    }
    
    public virtual void Deactivate() {
        gameObject.SetActive(false);
    }

    public virtual T GetAbility<T>() where T : HeroAbility {
        if (!cachedAbilities.ContainsKey(typeof(T))) {
            cachedAbilities.Add(typeof(T), abilities.Find(ab => ab is T));
        }
        return cachedAbilities[typeof(T)] as T;
    }
    
    protected virtual void FindAbilities() {
        foreach (Transform child in abilitiesContainer) {
            if (child.TryGetComponent(out HeroAbility ab)) {
                abilities.Add(ab);
            }
        }
    }

    protected virtual void SetUpModel() {
        foreach (Transform child in model) {
            if (child.TryGetComponent(out Mecanim _)) {
                Destroy(child.gameObject);
            }
        }
        mecanim = Instantiate(trait.mecanim, model);
    }
    
    protected virtual void Process() {
        foreach (var ab in abilities) {
            if (ab.IsActive) {
                ab.Process();
            }
        }
    }

    public virtual void SwitchCanvas(bool value) {
        canvas.enabled = value;
    }
}