using System;
using System.Threading.Tasks;
using RExt.Extensions;
using UnityEngine;

public class SkillProcessor_Caitlyn : SkillProcessor {
    readonly float healByHpMul;
    readonly float healByDmgMul;
    readonly float duration;
    readonly float interval;
    readonly float atkSpdMul;
    readonly float atkSpdDuration;
    readonly string hotKey;
    readonly string effectKey;
    
    public SkillProcessor_Caitlyn(BattleHero hero) : base(hero) {
        animationLength = 6.5f;
        timers = new[] { 2.2f, 5.6f };
        
        var skillParams = hero.Trait.skillParams;
        healByHpMul = skillParams[0].value;
        healByDmgMul = skillParams[1].value;
        duration = skillParams[2].value;
        interval = skillParams[3].value;
        atkSpdMul = skillParams[4].value;
        atkSpdDuration = skillParams[5].value;
        
        var specialKeys = hero.Trait.specialKeys;
        hotKey = specialKeys[0];
        effectKey = specialKeys[1];
    }

    public override void Process(float timer) {
        if (timer >= timers[0] && skillExecuted == 0) {
            DrinkTea();
            skillExecuted++;
        }
        else if (timer >= timers[1] && skillExecuted == 1) {
            Focus();
            skillExecuted++;
        }
    }

    void DrinkTea() {
        if (!attributes.IsAlive) return;
        
        attributes.AddHealOverTime(
            HealOverTime.Create(
                hotKey,
                hero,
                attributes.MaxHp * healByHpMul + attributes.PhysicalDamage * healByDmgMul,
                Mathf.RoundToInt(duration / interval),
                interval
            ));
    }

    void Focus() {
        if (!attributes.IsAlive) return;
        
        attributes.AddAttributeModifier(
            AttributeModifierSet.Create(
                hero,
                effectKey,
                atkSpdDuration,
                new[] {
                    (AttributeModifierKey.AttackSpeed, ATK_SPD_MUL: atkSpdMul, AttributeModifier.Type.Percentage)
                }
            ));
    }
}