using System;
using System.Collections.Generic;
using UnityEngine;

public class SkillProcessor_Malphite : SkillProcessor {
    readonly float defMul;
    readonly float atkSpeedReduceMul;
    readonly float effectDuration;
    readonly float baseDmg;
    readonly float dmgMul;
    readonly float airborneDuration;
    readonly string effectKey;
    
    public SkillProcessor_Malphite(BattleHero hero) : base(hero) {
        animationLength = 4.7f;
        timers = new[] { 0.46f, 2.3f };
        
        var skillParams = hero.Trait.skillParams;
        defMul = skillParams[0].value;
        atkSpeedReduceMul = skillParams[1].value;
        effectDuration = skillParams[2].value;
        baseDmg = skillParams[3].value;
        dmgMul = skillParams[4].value;
        airborneDuration = skillParams[5].value;
        
        var specialKeys = hero.Trait.specialKeys;
        effectKey = specialKeys[0];

        drainEnergy = true;
        drainEnergyDelay = timers[0];
        drainEnergyDuration = effectDuration;
    }

    public override void Process(float timer) {
        if (timer >= timers[0] && skillExecuted == 0) {
            Strengthen();
            skillExecuted++;
        }
        else if (timer >= timers[1] && skillExecuted == 1) {
            Slam();
            skillExecuted++;
        }
    }

    void Strengthen() {
        if (!attributes.IsAlive) return;
        
        var vfx = VfxPool.Instance.GetVfx<ScalableVfx>("malphite_skill");
        var bone = hero.Mecanim.GetComponent<BodyParts>().GetBone("chest");
        vfx.SetScale(hero.Model.localScale);
        vfx.GetComponent<PositionBind>().SetTarget(bone, -2f);
        
        attributes.AddAttributeModifier(
            AttributeModifierSet.Create(
                hero,
                effectKey,
                effectDuration,
                new [] {
                    (AttributeModifierKey.Armor, defMul, AttributeModifier.Type.Percentage),
                    (AttributeModifierKey.Resistance, defMul, AttributeModifier.Type.Percentage),
                    (AttributeModifierKey.AttackSpeed, atkSpeedReduceMul, AttributeModifier.Type.Percentage),
                    (AttributeModifierKey.Tenacity, HeroTrait.MAX_TENACITY, AttributeModifier.Type.FixedValue),
                },
                onRemove: () => {
                    VfxPool.Instance.DestroyVfx(vfx);
                }
            ));
    }

    void Slam() {
        if (hero.Target == null) return;
        
        hero.Target.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Physical, false,
            scaledValues: new[] { (dmgMul, DamageType.Physical) },
            fixedValues: new[] { baseDmg }));
        
        hero.Target.GetAbility<HeroStatusEffects>().Airborne(airborneDuration);
    }
}