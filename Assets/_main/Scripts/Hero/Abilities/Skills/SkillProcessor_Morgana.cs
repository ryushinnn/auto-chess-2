using System;
using UnityEngine;

public class SkillProcessor_Morgana : SkillProcessor {
    readonly float healByHpMulMin;
    readonly float healByHpMulMax;
    readonly float healByDmgMul;

    public SkillProcessor_Morgana(BattleHero hero) : base(hero) {
        animationLength = 1;
        timers = new[] { 0.41f };
        
        var skillParams = hero.Trait.skillParams;
        healByHpMulMin = skillParams[0].value;
        healByHpMulMax = skillParams[1].value;
        healByDmgMul = skillParams[2].value;
    }

    public override void Process(float timer) {
        if (timer >= timers[0] && skillExecuted == 0) {
            OpenWings();
            skillExecuted++;
        }
    }

    void OpenWings() {
        if (!attributes.IsAlive) return;
        
        attributes.Heal(attributes.MagicalDamage * healByDmgMul 
                        + attributes.MaxHp * Mathf.Lerp(healByHpMulMin, healByHpMulMax, attributes.HpLostPercentage));
    }
}