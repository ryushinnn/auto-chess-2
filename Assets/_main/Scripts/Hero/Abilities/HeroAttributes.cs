using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using RExt.Extensions;
using Sirenix.OdinInspector;
using UnityEngine;
using Random = UnityEngine.Random;

public class HeroAttributes : HeroAbility {
    HeroStatusEffects effects;
    HeroMovement movement;
    HeroAttack attack;
    HeroMark mark;
    HeroSkill skill;
    
    [SerializeField] ProgressBar healthBar;
    [SerializeField] ProgressBar energyBar;
    [SerializeField] Transform hpTextParent;
    [SerializeField] Color allyHpColor, enemyHpColor;
    [SerializeField] Color normalEnergyColor, drainingEnergyColor;
    
    public bool IsAlive => isAlive;
    public float MaxHp => maxHp;
    public float Hp => hp;
    public float HpPercentage => Mathf.Clamp01(hp / maxHp);
    public float HpLostPercentage => 1 - HpPercentage;
    public float Energy => energy;
    public float Armor => armor;
    public float Resistance => resistance;
    public float AttackSpeed => attackSpeed;
    public float PhysicalDamage => physicalDamage;
    public float MagicalDamage => magicalDamage;
    public float MovementSpeed => movementSpeed;
    public float CriticalChance => criticalChance;
    public float CriticalDamage => criticalDamage;
    public float EnergyRegenEfficiency => energyRegenEfficiency;
    public float PhysicalPenetration => physicalPenetration;
    public float MagicalPenetration => magicalPenetration;
    public float LifeSteal => lifeSteal;
    public float Tenacity => tenacity;

    [SerializeField, ReadOnly] bool isAlive;
    [SerializeField, ReadOnly] float maxHp;
    [SerializeField, ReadOnly] float hp;
    [SerializeField, ReadOnly] float energy;
    [SerializeField, ReadOnly] float armor;
    [SerializeField, ReadOnly] float resistance;
    [SerializeField, ReadOnly] float attackSpeed;
    [SerializeField, ReadOnly] float physicalDamage;
    [SerializeField, ReadOnly] float magicalDamage;
    [SerializeField, ReadOnly] float movementSpeed;
    [SerializeField, ReadOnly] float criticalChance;
    [SerializeField, ReadOnly] float criticalDamage;
    [SerializeField, ReadOnly] float energyRegenEfficiency;
    [SerializeField, ReadOnly] float physicalPenetration;
    [SerializeField, ReadOnly] float magicalPenetration;
    [SerializeField, ReadOnly] float lifeSteal;
    [SerializeField, ReadOnly] float tenacity;
    
    [SerializeField, ReadOnly, TableList] List<AttributeModifierSet> modifierSets = new();
    [SerializeField, ReadOnly, TableList] List<AttributeModifierGroup> modifierGroups = new();
    [SerializeField, ReadOnly] List<DamageOverTime> damageOverTimes = new();
    [SerializeField, ReadOnly] List<HealOverTime> healOverTimes = new();

    public event Action OnDeath;

    float hpScale;
    float damageScale;
    
    bool isDrainingEnergy;
    Sequence drainEnergySeq;

    public override void ResetAll() {
        isAlive = true;
        hp = maxHp = hero.Trait.maxHp;
        energy = 0;
        armor = hero.Trait.armor;
        resistance = hero.Trait.resistance;
        attackSpeed = hero.Trait.attackSpeed;
        physicalDamage = hero.Trait.physicalDamage;
        magicalDamage = hero.Trait.magicalDamage;
        movementSpeed = hero.Trait.movementSpeed;
        criticalChance = HeroTrait.BASE_CRITICAL_CHANCE;
        criticalDamage = HeroTrait.BASE_CRITICAL_DAMAGE;
        energyRegenEfficiency = HeroTrait.BASE_ENERGY_REGEN_EFFICIENCY;
        physicalPenetration = HeroTrait.BASE_PENETRATION;
        magicalPenetration = HeroTrait.BASE_PENETRATION;
        lifeSteal = HeroTrait.BASE_LIFE_STEAL;
        tenacity = HeroTrait.BASE_TENACITY;
        
        hero.SwitchCanvas(true);
        healthBar.UpdateAmount(1, true);
        energyBar.UpdateAmount(0, true);
        
        hpScale = 1;
        damageScale = 1;
        isDrainingEnergy = false;
        
        modifierSets.Clear();
        modifierGroups.Clear();
        damageOverTimes.Clear();
        healOverTimes.Clear();

        OnDeath = null;

        if (hero.Trait.evolutions != null) {
            var evolution = hero.Trait.evolutions.Find(x => x.rank == hero.Rank);
            if (evolution != null) {
                var modifiers = new (string, float, AttributeModifier.Type)[evolution.modifiers.Length];
                for (int i = 0; i < evolution.modifiers.Length; i++) {
                    var m = evolution.modifiers[i];
                    modifiers[i] = (m.key, m.value, m.type);
                }
    
                var modifiersSet = AttributeModifierSet.Create(
                    hero, 
                    $"[EVOLUTION_{evolution.rank}]", 
                    modifiers,
                    createMark: false);

                AddAttributeModifier(modifiersSet);
            }
        }
        
        healthBar.SetMainColor(hero is LineUpHero || ((BattleHero)hero).Side == TeamSide.Ally ? allyHpColor : enemyHpColor);
        energyBar.SetMainColor(normalEnergyColor);
    }

    public override void Process() {
        ProcessAttributeModifiers();
        ProcessDamageOverTimes();
        ProcessHealOverTimes();
    }

    protected override void FindReferences() {
        effects = hero.GetAbility<HeroStatusEffects>();
        movement = hero.GetAbility<HeroMovement>();
        attack = hero.GetAbility<HeroAttack>();
        mark = hero.GetAbility<HeroMark>();
        skill = hero.GetAbility<HeroSkill>();
    }

    public void SetPowerScales(float hpScale, float damageScale) {
        this.hpScale = hpScale;
        this.damageScale = damageScale;
        maxHp *= this.hpScale;
        hp = maxHp;
        physicalDamage *= this.damageScale;
        magicalDamage *= this.damageScale;
    }

    public float TakeDamage(Damage damage, bool regenEnergy = true) {
        return TakeDamage(damage, regenEnergy, true).value;
    }
    
    public float TakeDamage(Damage[] damages, bool regenEnergy = true) {
        var outputDmgs = new List<Damage>();
        foreach (var dmg in damages) {
            outputDmgs.Add(TakeDamage(dmg, false, false));
        }

        if (hp > 0 && regenEnergy) {
            RegenEnergy(hero.Trait.energyRegenPerHit);
        }
        HpTextSpawner.Instance.SpawnHpTextAsDamage(hpTextParent, outputDmgs);
        return outputDmgs.Sum(x=>x.value);
    }

    Damage TakeDamage(Damage damage, bool regenEnergy, bool showText) {
        
        //                              pre-mitigation dmg
        // post-mitigation dmg =   _______________________________
        //                                   armor/resistance
        //                         1 +   _________________________
        //                               DAMAGE_REDUCTION_CONSTANT
        
        var finalDmg = Damage.Create(
            Mathf.Max(damage.type switch {
                        DamageType.Physical => damage.value / (1 + Mathf.Max(HeroTrait.MIN_ARMOR_AND_RESISTANCE, armor - damage.penetration) / HeroTrait.DAMAGE_REDUCTION_CONSTANT),
                        DamageType.Magical => damage.value / (1 + Mathf.Max(HeroTrait.MIN_ARMOR_AND_RESISTANCE, resistance - damage.penetration) / HeroTrait.DAMAGE_REDUCTION_CONSTANT),
                        DamageType.True => damage.value
                    },
                HeroTrait.MIN_DAMAGE_DEALT), 
            damage.type, 
            damage.penetration, 
            damage.crit
        );
        
        hp -= finalDmg.value;
        healthBar.UpdateAmount(hp / maxHp);
        
        if (hp > 0) {
            if (regenEnergy) {
                RegenEnergy(hero.Trait.energyRegenPerHit);
            }
        }
        else {
            Die();
        }

        if (showText) {
            HpTextSpawner.Instance.SpawnHpTextAsDamage(hpTextParent,finalDmg);
        }
        return finalDmg;
    }

    public void Heal(float amount) {
        if (!isAlive) return;

        if (0 < amount && amount < 1) {
            amount = 1;
        }
        
        if (effects.IsAntiHeal) {
            amount *= (1 - HeroTrait.ANTI_HEAL_EFFICIENCY);
        }

        hp = Mathf.Min(hp + amount, maxHp);
        healthBar.UpdateAmount(hp / maxHp);
        
        HpTextSpawner.Instance.SpawnHpTextAsHeal(hpTextParent,amount);
    }

    public void RegenEnergy(float amount) {
        if (!isAlive || isDrainingEnergy || skill.IsUsingSkill) return;
        
        amount *= energyRegenEfficiency;
        energy = Mathf.Min(energy + amount, HeroTrait.MAX_ENERGY);
        energyBar.UpdateAmount(energy / HeroTrait.MAX_ENERGY);
    }

    public void UseAllEnergy() {
        energy = 0;
        energyBar.UpdateAmount(0);
    }

    public void DrainEnergy(float delay, float duration) {
        energy = 0;
        isDrainingEnergy = true;
        energyBar.SetMainColor(drainingEnergyColor);
        drainEnergySeq?.Kill();
        drainEnergySeq = DOTween.Sequence()
            .AppendInterval(delay)
            .Append(DOVirtual.Float(1,0,duration, val => {
                energyBar.UpdateAmount(val);
            }).SetEase(Ease.Linear))
            .AppendCallback(() => {
                isDrainingEnergy = false;
                energyBar.SetMainColor(normalEnergyColor);
            });
    }

    public bool Crit() {
        return Random.value < criticalChance;
    }
    
    public Damage GetDamage(DamageType outputType, bool crit, float[] fixedValues = null, (float perc, DamageType type)[] scaledValues = null) {
        var pen = outputType switch {
            DamageType.Physical => PhysicalPenetration,
            DamageType.Magical => MagicalPenetration,
            DamageType.True => 0
        };

        var value = 0f;

        if (fixedValues.IsNotEmpty()) {
            value = fixedValues.Sum();
        }

        if (scaledValues.IsNotEmpty()) {
            foreach (var val in scaledValues) {
                if (val.type == DamageType.True) {
                    Debug.LogError("True Damage can't be used as input type");
                    continue;
                }
                value += val.type switch {
                    DamageType.Physical => PhysicalDamage * val.perc,
                    DamageType.Magical => MagicalDamage * val.perc,
                };
            }
        }

        if (crit) {
            value *= CriticalDamage;
        }

        return Damage.Create(value, outputType, pen, crit);
    }

    public Damage GetDamage(DamageType outputType) {
        if (outputType == DamageType.True) {
            Debug.LogError("True Damage can't be used as input type");
            return Damage.Create(0, outputType, 0);
        }
        
        var pen = outputType switch {
            DamageType.Physical => PhysicalPenetration,
            DamageType.Magical => MagicalPenetration,
        };
        
        var value = outputType switch {
            DamageType.Physical => PhysicalDamage,
            DamageType.Magical => MagicalDamage,
        };

        var crit = Crit();
        if (crit) {
            value *= CriticalDamage;
        }
        
        return Damage.Create(value, outputType, pen, crit);
    }
    
    public void AddAttributeModifier(AttributeModifierSet modifierSet) {
        var existSet = modifierSets.Find(x => x.SameAs(modifierSet));
        if (existSet != null) {
            foreach (var modifier in existSet.modifiers) {
                RemoveAttributeModifier(modifier);
            }
            if (existSet.mark != null) {
                mark.RemoveMark(existSet.mark);
            }
            modifierSets.Remove(existSet);
        }
        
        modifierSets.Add(modifierSet);
        foreach (var modifier in modifierSet.modifiers) {
            AddAttributeModifier(modifier);
        }

        if (modifierSet.mark != null) {
            mark.AddMark(modifierSet.mark);
        }
    }
    
    void AddAttributeModifier(AttributeModifier modifier) {
        var group = modifierGroups.Find(x => x.key == modifier.key);
        if (group == null) {
            group = new AttributeModifierGroup(modifier.key);
            modifierGroups.Add(group);
        }
        group.modifiers.Add(modifier);
        RecalculateAttributes(modifier.key);
    }

    void RemoveAttributeModifier(AttributeModifier modifier) {
        for (int i = modifierGroups.Count - 1; i >= 0; i--) {
            var key = modifierGroups[i].key;
            if (modifierGroups[i].modifiers.Contains(modifier)) {
                modifierGroups[i].modifiers.Remove(modifier);
                if (modifierGroups[i].modifiers.Count == 0) {
                    modifierGroups.RemoveAt(i);
                }
                RecalculateAttributes(key);
                return;
            }
        }
    }
    
    void ProcessAttributeModifiers() {
        var changedKeys = new HashSet<string>();

        for (int i = modifierSets.Count - 1; i >= 0; i--) {
            if (modifierSets[i].permanent) continue;
            modifierSets[i].duration -= Time.deltaTime;
            if (modifierSets[i].duration <= 0) {
                foreach (var m in modifierSets[i].modifiers) {
                    changedKeys.Add(m.key);
                    var group = modifierGroups.Find(x=>x.key == m.key);
                    group.modifiers.Remove(m);
                    if (group.modifiers.Count == 0) {
                        modifierGroups.Remove(group);
                    }
                }
                if (modifierSets[i].mark != null) {
                    mark.RemoveMark(modifierSets[i].mark);
                }
                modifierSets[i].onRemove?.Invoke();
                modifierSets.RemoveAt(i);
            }
        }

        foreach (var key in changedKeys) {
            RecalculateAttributes(key);
        }
    }

    public void AddDamageOverTime(DamageOverTime dot) {
        for (int i = damageOverTimes.Count - 1; i >= 0; i--) {
            if (damageOverTimes[i].SameAs(dot)) {
                if (damageOverTimes[i].mark != null) {
                    mark.RemoveMark(damageOverTimes[i].mark);
                }
                damageOverTimes.RemoveAt(i);
            }
        }
        damageOverTimes.Add(dot);
        if (dot.mark != null) {
            mark.AddMark(dot.mark);
        }
    }
    
    void ProcessDamageOverTimes() {
        for (int i = damageOverTimes.Count - 1; i >= 0; i--) {
            var dot = damageOverTimes[i];
            if (dot.timer <= 0) {
                TakeDamage(dot.damage, false);
                if (--dot.times <= 0) {
                    damageOverTimes.Remove(dot);
                    if (dot.mark != null) {
                        mark.RemoveMark(dot.mark);
                    }
                }
                else {
                    dot.timer = dot.interval;
                }
            }
            else {
                dot.timer -= Time.deltaTime;
            }
        }
    }
    
    public void AddHealOverTime(HealOverTime hot) {
        for (int i = healOverTimes.Count - 1; i >= 0; i--) {
            if (healOverTimes[i].SameAs(hot)) {
                if (healOverTimes[i].mark != null) {
                    mark.RemoveMark(healOverTimes[i].mark);
                }
                healOverTimes.RemoveAt(i);
            }
        }
        healOverTimes.Add(hot);
        if (hot.mark != null) {
            mark.AddMark(hot.mark);
        }
    }
    
    void ProcessHealOverTimes() {
        for (int i = healOverTimes.Count - 1; i >= 0; i--) {
            var hot = healOverTimes[i];
            if (hot.timer <= 0) {
                Heal(hot.amount);
                if (--hot.times <= 0) {
                    healOverTimes.Remove(hot);
                    if (hot.mark != null) {
                        mark.RemoveMark(hot.mark);
                    }
                }
                else {
                    hot.timer = hot.interval;
                }
            }
            else {
                hot.timer -= Time.deltaTime;
            }
        }
    }

    void RecalculateAttributes(string key) {
        var modifiers = modifierGroups.Find(x => x.key == key)?.modifiers;
        modifiers?.Sort((a, b) => {
            var typeComparison = a.type == AttributeModifier.Type.FixedValue ?
                (b.type == AttributeModifier.Type.FixedValue ? 0 : -1) :
                (b.type == AttributeModifier.Type.FixedValue ? 1 : 0);
            
            if (typeComparison != 0) return typeComparison;
            return b.value.CompareTo(a.value);
        });
        
        switch (key) {
            case AttributeModifierKey.MaxHp:
                var lastMaxHp = maxHp;
                maxHp = hero.Trait.maxHp * hpScale;
                modifiers?.ForEach(x => {
                    maxHp += (x.type == AttributeModifier.Type.FixedValue ? x.value : maxHp * x.value);
                });
                if (maxHp > lastMaxHp) {
                    hp += maxHp - lastMaxHp;
                }
                else {
                    hp = Mathf.Min(hp, maxHp);
                }
                healthBar.UpdateAmount(hp / maxHp);
                break;
            
            case AttributeModifierKey.PhysicalDamage:
                physicalDamage = hero.Trait.physicalDamage * damageScale;
                modifiers?.ForEach(x => {
                    physicalDamage = Mathf.Max(physicalDamage + (x.type == AttributeModifier.Type.FixedValue ? x.value : physicalDamage * x.value), HeroTrait.MIN_DAMAGE);
                });
                break;
            
            case AttributeModifierKey.MagicalDamage:
                magicalDamage = hero.Trait.magicalDamage * damageScale;
                modifiers?.ForEach(x => {
                    magicalDamage = Mathf.Max(magicalDamage + (x.type == AttributeModifier.Type.FixedValue ? x.value : magicalDamage * x.value), HeroTrait.MIN_DAMAGE);
                });
                break;
            
            case AttributeModifierKey.Armor:
                armor = hero.Trait.armor;
                modifiers?.ForEach(x => {
                    armor = Mathf.Max(armor + (x.type == AttributeModifier.Type.FixedValue ? x.value : armor * x.value), HeroTrait.MIN_ARMOR_AND_RESISTANCE);
                });
                break;
            
            case AttributeModifierKey.Resistance:
                resistance = hero.Trait.resistance;
                modifiers?.ForEach(x => {
                    resistance = Mathf.Max(resistance + (x.type == AttributeModifier.Type.FixedValue ? x.value : resistance * x.value), HeroTrait.MIN_ARMOR_AND_RESISTANCE);
                });
                break;
            
            case AttributeModifierKey.AttackSpeed:
                attackSpeed = hero.Trait.attackSpeed;
                modifiers?.ForEach(x => {
                    attackSpeed = Mathf.Max(attackSpeed + (x.type == AttributeModifier.Type.FixedValue ? x.value : attackSpeed * x.value), HeroTrait.MIN_ATTACK_SPEED);
                });
                attack?.RefreshAttackCooldown();
                hero.Mecanim.ModifyAttackTime(attackSpeed);
                break;
            
            case AttributeModifierKey.CriticalChance:
                criticalChance = HeroTrait.BASE_CRITICAL_CHANCE;
                modifiers?.ForEach(x => {
                    criticalChance += (x.type == AttributeModifier.Type.FixedValue ? x.value : criticalChance * x.value);
                });
                criticalChance = Mathf.Min(criticalChance, HeroTrait.MAX_CRITICAL_CHANCE);
                break;
            
            case AttributeModifierKey.CriticalDamage:
                criticalDamage = HeroTrait.BASE_CRITICAL_DAMAGE;
                modifiers?.ForEach(x => {
                    criticalDamage += (x.type == AttributeModifier.Type.FixedValue ? x.value : criticalDamage * x.value);
                });
                break;
            
            case AttributeModifierKey.EnergyRegenEfficient:
                energyRegenEfficiency = HeroTrait.BASE_ENERGY_REGEN_EFFICIENCY;
                modifiers?.ForEach(x => {
                    energyRegenEfficiency += (x.type == AttributeModifier.Type.FixedValue ? x.value : energyRegenEfficiency * x.value);
                });
                break;
            
            case AttributeModifierKey.PhysicalPenetration:
                physicalPenetration = HeroTrait.BASE_PENETRATION;
                modifiers?.ForEach(x => {
                    physicalPenetration = Mathf.Min(physicalPenetration + (x.type == AttributeModifier.Type.FixedValue ? x.value : physicalPenetration * x.value), HeroTrait.MAX_PENETRATION);
                });
                break;
            
            case AttributeModifierKey.MagicalPenetration:
                magicalPenetration = HeroTrait.BASE_PENETRATION;
                modifiers?.ForEach(x => {
                    magicalPenetration = Mathf.Min(magicalPenetration + (x.type == AttributeModifier.Type.FixedValue ? x.value : magicalPenetration * x.value), HeroTrait.MAX_PENETRATION);
                });
                break;
            
            case AttributeModifierKey.LifeSteal:
                lifeSteal = HeroTrait.BASE_LIFE_STEAL;
                modifiers?.ForEach(x => {
                    lifeSteal = Mathf.Min(lifeSteal + (x.type == AttributeModifier.Type.FixedValue ? x.value : lifeSteal * x.value), HeroTrait.MAX_LIFE_STEAL);
                });
                break;
            
            case AttributeModifierKey.Tenacity:
                tenacity = HeroTrait.BASE_TENACITY;
                modifiers?.ForEach(x => {
                    tenacity = Mathf.Min(tenacity + (x.type == AttributeModifier.Type.FixedValue ? x.value : tenacity * x.value), HeroTrait.MAX_TENACITY);
                });
                break;
        }
    }

    void Die() {
        isAlive = false;
        drainEnergySeq?.Kill();

        foreach (var ms in modifierSets) {
            ms.onRemove?.Invoke();
        }
        
        movement.StopMove(true);
        hero.Mecanim.Death();
        hero.Mecanim.InterruptAttack();
        hero.Mecanim.InterruptSkill();
        hero.SwitchCanvas(false);
        hero.name = "(DEAD) " + hero.name;
        GameManager.Instance.BattleField.MarkHeroAsDead((BattleHero)hero);
        
        OnDeath?.Invoke();
    }
}

[Serializable]
public class AttributeModifierGroup {
    [TableColumnWidth(100, resizable:false)] public string key;
    public List<AttributeModifier> modifiers;

    public AttributeModifierGroup(string key) {
        this.key = key;
        modifiers = new List<AttributeModifier>();
    }
}