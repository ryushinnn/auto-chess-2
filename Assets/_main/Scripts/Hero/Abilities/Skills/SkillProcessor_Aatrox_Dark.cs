using System;

public class SkillProcessor_Aatrox_Dark : SkillProcessor {
    readonly float baseDmg;
    readonly float dmgMul0;
    readonly float dmgMul1;
    readonly float dmgMul2;
    readonly float airborneDuration0;
    readonly float airborneDuration1;
    readonly float airborneDuration2;
    readonly float armorReduceMul;
    readonly float armorReduceDuration;
    readonly float hpThreshold;
    readonly float baseTrueDmg;
    readonly float trueDmgMul;
    readonly string effectKey;

    public SkillProcessor_Aatrox_Dark(BattleHero hero) : base(hero) {
        animationLength = 5;
        timers = new[] { 0.6f, 2.3f, 4.1f };
        
        var skillParams = hero.Trait.skillParams;
        baseDmg = skillParams[2].value;
        dmgMul0 = skillParams[3].value;
        dmgMul1 = skillParams[4].value;
        dmgMul2 = skillParams[5].value;
        airborneDuration0 = skillParams[6].value;
        airborneDuration1 = skillParams[7].value;
        airborneDuration2 = skillParams[8].value;
        armorReduceMul = skillParams[9].value;
        armorReduceDuration = skillParams[10].value;
        hpThreshold = skillParams[11].value;
        baseTrueDmg = skillParams[12].value;
        trueDmgMul = skillParams[13].value;
        
        var specialKeys = hero.Trait.specialKeys;
        effectKey = specialKeys[0];
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
        
        hero.Target.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Physical, false,
            scaledValues: new[] { (dmgMul0, DamageType.Physical) },
            fixedValues: new[] { baseDmg })
        );
        
        if (hero.Target.GetAbility<HeroAttributes>().HpPercentage < hpThreshold) {
            hero.Target.GetAbility<HeroAttributes>().AddAttributeModifier(
                AttributeModifierSet.Create(
                    hero,
                    effectKey,
                    armorReduceDuration,
                    new[] {
                        (AttributeModifierKey.Armor, ARMOR_REDUCE_MUL: armorReduceMul, AttributeModifier.Type.Percentage)
                    }
                ));
        }
        
        hero.Target.GetAbility<HeroStatusEffects>().Airborne(airborneDuration0);
    }

    void MediumSlash() {
        if (hero.Target == null) return;
        
        if (hero.Target.GetAbility<HeroAttributes>().HpPercentage >= hpThreshold) {
            hero.Target.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Physical, false,
                scaledValues: new[] { (dmgMul1, DamageType.Physical) },
                fixedValues: new[] { baseDmg })
            );
        }
        else {
            hero.Target.GetAbility<HeroAttributes>().TakeDamage(
                new[] {
                    attributes.GetDamage(DamageType.Physical, false, 
                        scaledValues: new[] {(dmgMul1, DamageType.Physical)},
                        fixedValues: new[] { baseDmg }),
                    attributes.GetDamage(DamageType.True, false, 
                        scaledValues: new[] {(trueDmgMul, DamageType.Physical)},
                        fixedValues: new[] { baseTrueDmg })
                });
        }
        
        hero.Target.GetAbility<HeroStatusEffects>().Airborne(airborneDuration1);
    }
    
    void HeavySlash() {
        if (hero.Target == null) return;
        
        hero.Target.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Physical, attributes.Crit(),
            scaledValues: new[] { (dmgMul2, DamageType.Physical) },
            fixedValues: new[] { baseDmg })
        );
        
        hero.Target.GetAbility<HeroStatusEffects>().Airborne(airborneDuration2);
    }
}