using System;
using UnityEngine;

public class SkillProcessor_Aatrox_Light : SkillProcessor {
    readonly float baseDmg;
    readonly float dmgMul0;
    readonly float dmgMul1;
    readonly float dmgMul2;
    readonly float airborneTime0;
    readonly float airborneTime1;
    readonly float airborneTime2;
    readonly float vamp0;
    readonly float vamp1;
    readonly float vamp2;
    readonly float vampMulOnLowHp;
    readonly float hpThreshold;

    public SkillProcessor_Aatrox_Light(BattleHero hero) : base(hero) {
        animationLength = 5f;
        timers = new[] { 0.6f, 2.3f, 4.1f };
        
        var skillParams = hero.Trait.skillParams;
        dmgMul0 = skillParams[0].value;
        dmgMul1 = skillParams[1].value;
        dmgMul2 = skillParams[2].value;
        airborneTime0 = skillParams[3].value;
        airborneTime1 = skillParams[4].value;
        airborneTime2 = skillParams[5].value;
        vamp0 = skillParams[6].value;
        vamp1 = skillParams[7].value;
        vamp2 = skillParams[8].value;
        vampMulOnLowHp = skillParams[9].value;
        hpThreshold = skillParams[10].value;
    }
    
    public override void Process(float timer) {
        if (timer >= timers[0] && skillExecuted == 0) {
            LightSlash();
            skillExecuted++;
        }
        else if (timer >= timers[1] && skillExecuted == 1) {
            MediumSlash();
            skillExecuted++;
        }
        else if (timer >= timers[2] && skillExecuted == 2) {
            HeavySlash();
            skillExecuted++;
        }
    }

    void LightSlash() {
        if (hero.Target == null) return;

        var outputDmg = hero.Target.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Magical, false,
            scaledValues: new []{(dmgMul0, DamageType.Magical)},
            fixedValues: new []{ baseDmg }));
        
        var vamp = attributes.HpPercentage < hpThreshold ? vamp0 * vampMulOnLowHp : vamp0;
        attributes.Heal(outputDmg * vamp);
        hero.Target.GetAbility<HeroStatusEffects>().Airborne(airborneTime0);
    }
    
    void MediumSlash() {
        if (hero.Target == null) return;
        
        var outputDmg = hero.Target.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Magical, false,
            scaledValues: new []{(dmgMul1, DamageType.Magical)},
            fixedValues: new []{ baseDmg }));
        
        var vamp = attributes.HpPercentage < hpThreshold ? vamp1 * vampMulOnLowHp : vamp1;
        attributes.Heal(outputDmg * vamp);
        hero.Target.GetAbility<HeroStatusEffects>().Airborne(airborneTime1);
    }
    
    void HeavySlash() {
        if (hero.Target == null) return;
        
        var outputDmg = hero.Target.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Magical, false,
            scaledValues: new []{(dmgMul2, DamageType.Magical)},
            fixedValues: new []{ baseDmg }));
        
        var vamp = attributes.HpPercentage < hpThreshold ? vamp2 * vampMulOnLowHp : vamp2;
        attributes.Heal(outputDmg * vamp);
        hero.Target.GetAbility<HeroStatusEffects>().Airborne(airborneTime2);
    }
}