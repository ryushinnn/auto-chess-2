using System;
using RExt.Extensions;
using UnityEngine;

public class SkillProcessor_Yasuo : SkillProcessor {
    readonly float airborneDuration;
    readonly float baseDmg0;
    readonly float dmgMul0;
    readonly float baseDmg1;
    readonly float dmgMul1;
    readonly float dotBaseDmg;
    readonly float dotDmgMul;
    readonly float duration;
    readonly float interval;
    readonly string dotKey;
    
    public SkillProcessor_Yasuo(BattleHero hero) : base(hero) {
        animationLength = 6.3f;
        timers = new[] { 0.7f, 1.7f, 3.5f };

        var skillParams = hero.Trait.skillParams;
        airborneDuration = skillParams[0].value;
        baseDmg0 = skillParams[1].value;
        dmgMul0 = skillParams[2].value;
        baseDmg1 = skillParams[3].value;
        dmgMul1 = skillParams[4].value;
        dotBaseDmg = skillParams[5].value;
        dotDmgMul = skillParams[6].value;
        duration = skillParams[7].value;
        interval = skillParams[8].value;
        
        var specialKeys = hero.Trait.specialKeys;
        dotKey = specialKeys[0];
    }

    public override void Process(float timer) {
        if (timer >= timers[0] && skillExecuted == 0) {
            BlowUp();
            skillExecuted++;
        }
        else if (timer >= timers[1] && skillExecuted == 1) {
            Cut();
            skillExecuted++;
        }
        else if (timer >= timers[2] && skillExecuted == 2) {
            BonusCut();
            skillExecuted++;
        }
    }

    void BlowUp() {
        if (hero.Target == null) return;

        hero.Target.GetAbility<HeroStatusEffects>().Airborne(airborneDuration);
    }

    void Cut() {
        if (hero.Target == null) return;

        hero.Target.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Physical, false,
            scaledValues:new[]{(dmgMul0, DamageType.Physical)},
            fixedValues:new[]{ baseDmg0 }));
    }

    void BonusCut() {
        if (hero.Target == null) return;
        
        hero.Target.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Physical, false,
            scaledValues:new[]{(dmgMul1, DamageType.Physical)},
            fixedValues:new[]{ baseDmg1 }));

        var bleedDmg = attributes.GetDamage(DamageType.Physical, false,
            scaledValues: new[] { (dotDmgMul, DamageType.Physical) },
            fixedValues: new[] { dotBaseDmg });
        
        hero.Target.GetAbility<HeroAttributes>().AddDamageOverTime(
            DamageOverTime.Create(
                dotKey,
                hero,
                bleedDmg,
                Mathf.RoundToInt(duration / interval),
                interval,
                1,
                false,
                true
            ));
    }
}