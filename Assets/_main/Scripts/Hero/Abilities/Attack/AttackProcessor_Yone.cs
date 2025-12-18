using System;
using System.Collections.Generic;
using RExt.Extensions;
using UnityEngine;

public class AttackProcessor_Yone : AttackProcessor {
    readonly float dmgReduceMul;
    readonly int maxStacks;
    readonly float duration;
    readonly float vampMin;
    readonly float vampMax;
    readonly string effectKey;
    
    public YoneSword CurrentSword { get; private set; }

    public AttackProcessor_Yone(BattleHero hero) : base(hero) {
        animationLength = 1.367f;
        timers = new[] { 0.5f };
        
        var skillParams = hero.Trait.skillParams;
        dmgReduceMul = skillParams[0].value;
        maxStacks = (int)skillParams[1].value;
        duration = skillParams[2].value;
        vampMin = skillParams[3].value;
        vampMax = skillParams[4].value;
        
        var specialKeys = hero.Trait.specialKeys;
        effectKey = specialKeys[0];
        
        CurrentSword = YoneSword.Divine;
    }

    public override void Process(float timer) {
        base.Process(timer);

        if (trueTimer >= timers[0] && atkExecuted == 0) {
            if (hero.Target != null) {
                Damage dmg;
                if (CurrentSword == YoneSword.Divine) {
                    dmg = attributes.GetDamage(DamageType.Physical);
                }
                else {
                    dmg = attributes.GetDamage(DamageType.Magical, attributes.Crit(),
                        scaledValues: new[] { (1f, DamageType.Physical) });
                }
                
                var outputDmg = hero.Target.GetAbility<HeroAttributes>().TakeDamage(dmg);
                var heal = outputDmg * attributes.LifeSteal;
                if (CurrentSword == YoneSword.Devil) {
                    heal += outputDmg * Mathf.Lerp(vampMin, vampMax, attributes.HpLostPercentage);
                }

                if (heal > 0) {
                    attributes.Heal(heal);
                }

                if (CurrentSword == YoneSword.Divine) {
                    var currentStack = hero.Target.GetAbility<HeroMark>().GetMark(effectKey, hero)?.stacks ?? 0;
                    var nextStacks = Mathf.Min(currentStack + 1, maxStacks);
                    
                    hero.Target.GetAbility<HeroAttributes>().AddAttributeModifier(
                        AttributeModifierSet.Create(
                            hero,
                            effectKey,
                            duration,
                            new[] {
                                (AttributeModifierKey.PhysicalDamage, dmgReduceMul, AttributeModifier.Type.Percentage),
                                (AttributeModifierKey.MagicalDamage, dmgReduceMul, AttributeModifier.Type.Percentage),
                            },
                            nextStacks
                        ));
                }
                
                attributes.RegenEnergy(hero.Trait.energyRegenPerAttack);
            }
            CurrentSword = CurrentSword.Next();
            atkExecuted++;
        }
    }
}

public enum YoneSword {
    Divine,
    Devil
}