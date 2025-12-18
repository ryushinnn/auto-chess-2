using System;
using UnityEngine;

public class AttackProcessor_Aatrox_Dark : AttackProcessor {
    readonly float dmgMulMin;
    readonly float dmgMulMax;

    public AttackProcessor_Aatrox_Dark(BattleHero hero) : base(hero) {
        animationLength = 2.167f;
        timers = new[] { 0.9f, 1.9f };
        
        var skillParams = hero.Trait.skillParams;
        dmgMulMin = skillParams[0].value;
        dmgMulMax = skillParams[1].value;
    }

    public override void Process(float timer) {
        base.Process(timer);

        if (trueTimer >= timers[0] && atkExecuted == 0) {
            if (hero.Target != null) {
                var targetAtb = hero.Target.GetAbility<HeroAttributes>();
                var perc = Mathf.Lerp(dmgMulMin, dmgMulMax, targetAtb.HpLostPercentage);
                var baseDmg = attributes.GetDamage(DamageType.Physical, false, scaledValues: new[] {
                    (perc, DamageType.Physical)
                });
                var outputDmg = targetAtb.TakeDamage(baseDmg);
                var heal = outputDmg * attributes.LifeSteal;
                if (heal > 0) {
                    attributes.Heal(heal);
                }
            }
            atkExecuted++;
        }
        else if (trueTimer >= timers[1] && atkExecuted == 1) {
            if (hero.Target != null) {
                var targetAtb = hero.Target.GetAbility<HeroAttributes>();
                var perc = Mathf.Lerp(dmgMulMin, dmgMulMax, targetAtb.HpLostPercentage);
                var baseDmg = attributes.GetDamage(DamageType.Physical, true, scaledValues: new[] {
                    (perc, DamageType.Physical)
                });
                var outputDmg = targetAtb.TakeDamage(baseDmg);
                var heal = outputDmg * attributes.LifeSteal;
                if (heal > 0) {
                    attributes.Heal(heal);
                }
            }
            atkExecuted++;
            attributes.RegenEnergy(hero.Trait.energyRegenPerAttack);
        }
    }
}