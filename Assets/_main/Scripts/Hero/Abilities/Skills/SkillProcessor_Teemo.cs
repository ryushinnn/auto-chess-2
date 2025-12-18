using System;
using RExt.Extensions;
using UnityEngine;

public class SkillProcessor_Teemo : SkillProcessor {
    readonly int maxStacks;
    readonly float duration;
    readonly float interval;
    readonly float dmgByMaxHp;
    readonly float dmgMulLimit;
    readonly string dotKey;
    
    readonly float baseDmg;
    readonly float dmgMul;
    readonly float bonusBaseDmgPerStack;
    readonly float bonusDmgMulPerStack;
    
    public SkillProcessor_Teemo(BattleHero hero) : base(hero) {
        animationLength = 4;
        timers = new[] { 1.56f, 2.73f, 3.29f };
        
        var skillParams = hero.Trait.skillParams;
        maxStacks = (int)skillParams[0].value;
        duration = skillParams[1].value;
        interval = skillParams[2].value;
        dmgByMaxHp = skillParams[3].value;
        dmgMulLimit = skillParams[4].value;
        baseDmg = skillParams[5].value;
        dmgMul = skillParams[6].value;
        bonusBaseDmgPerStack = skillParams[7].value;
        bonusDmgMulPerStack = skillParams[8].value;
        
        var specialKeys = hero.Trait.specialKeys;
        dotKey = specialKeys[0];
    }

    public override void Process(float timer) {
        if (timer >= timers[0] && skillExecuted == 0) {
            ThrowBomb();
            skillExecuted++;
        }
        else if (timer >= timers[1] && skillExecuted == 1) {
            ThrowBomb();
            skillExecuted++;
        }
        else if (timer >= timers[2] && skillExecuted == 2) {
            ThrowBigBomb();
            skillExecuted++;
        }
    }

    void ThrowBomb() {
        if (hero.Target == null) return;

        var currentStacks = hero.Target.GetAbility<HeroMark>().GetMark(dotKey, hero)?.stacks ?? 0;
        var nextStacks = Mathf.Min(currentStacks + 1, maxStacks);
        
        var igniteDmg = Damage.Create(
            nextStacks * Mathf.Min(attributes.MagicalDamage * dmgMulLimit, hero.Target.GetAbility<HeroAttributes>().MaxHp * dmgByMaxHp),
            DamageType.True,
            0
        );

        var mainDmg = attributes.GetDamage(DamageType.Magical, false,
            scaledValues: new[] {
                (dmgMul, DamageType.Magical),
                (bonusDmgMulPerStack * currentStacks, DamageType.Magical)
            },
            fixedValues: new[] {
                baseDmg, 
                bonusBaseDmgPerStack * currentStacks
            });

        hero.Target.GetAbility<HeroAttributes>().TakeDamage(new[] { mainDmg, igniteDmg });
        
        hero.Target.GetAbility<HeroAttributes>().AddDamageOverTime(
            DamageOverTime.Create(
                    dotKey,
                    hero,
                    igniteDmg, 
                    Mathf.RoundToInt(duration / interval) - 1,
                    interval,
                    nextStacks,
                    false,
                    true
                ));
    }

    void ThrowBigBomb() {
        if (hero.Target == null) return;
        
        var currentStacks = hero.Target.GetAbility<HeroMark>().GetMark(dotKey, hero)?.stacks ?? 0;
        var nextStacks = Mathf.Min(currentStacks + 1, maxStacks);

        var igniteDmg = Damage.Create(
            nextStacks * Mathf.Min(attributes.MagicalDamage * dmgMulLimit, hero.Target.GetAbility<HeroAttributes>().MaxHp * dmgByMaxHp),
            DamageType.True,
            0
        );

        var mainDmg = attributes.GetDamage(DamageType.Magical, true,
            scaledValues: new[] {
                (dmgMul, DamageType.Magical),
                (bonusDmgMulPerStack * currentStacks, DamageType.Magical)
            },
            fixedValues: new[] {
                baseDmg, 
                bonusBaseDmgPerStack * currentStacks
            });

        hero.Target.GetAbility<HeroAttributes>().TakeDamage(new[] { mainDmg, igniteDmg });
        
        hero.Target.GetAbility<HeroAttributes>().AddDamageOverTime(
            DamageOverTime.Create(
                dotKey,
                hero,
                igniteDmg,
                Mathf.RoundToInt(duration / interval) - 1,
                interval,
                nextStacks,
                false,
                true
            ));
    }
}