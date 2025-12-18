using System;
using RExt.Extensions;
using UnityEngine;

public class AttackProcessor_Teemo : AttackProcessor {
    readonly int maxStacks;
    readonly float duration;
    readonly float interval;
    readonly float dmgByMaxHp;
    readonly float dmgMulLimit;
    readonly string dotKey;

    public AttackProcessor_Teemo(BattleHero hero) : base(hero) {
        animationLength = 0.933f;
        timers = new[] { 0.17f };
        
        var skillParams = hero.Trait.skillParams;
        maxStacks = (int)skillParams[0].value;
        duration = skillParams[1].value;
        interval = skillParams[2].value;
        dmgByMaxHp = skillParams[3].value;
        dmgMulLimit = skillParams[4].value;
        
        var specialKeys = hero.Trait.specialKeys;
        dotKey = specialKeys[0];
    }

    public override void Process(float timer) {
        base.Process(timer);

        if (trueTimer >= timers[0] && atkExecuted == 0) {
            if (hero.Target != null) {
                var currentStacks = hero.Target.GetAbility<HeroMark>().GetMark(dotKey, hero)?.stacks ?? 0;
                var nextStacks = Mathf.Min(currentStacks + 1, maxStacks);

                var igniteDmg = Damage.Create(
                    nextStacks * Mathf.Min(attributes.MagicalDamage * dmgMulLimit, hero.Target.GetAbility<HeroAttributes>().MaxHp * dmgByMaxHp),
                    DamageType.True,
                    0
                );

                var outputDmg = hero.Target.GetAbility<HeroAttributes>().TakeDamage(
                    new[] {
                        attributes.GetDamage(DamageType.Magical),
                        igniteDmg,
                    }) - igniteDmg.value;
                // why subtract ignite dmg???
                // add it to TakeDamage just for nicer visual (2 HpText arranges vertically),
                // it doesn't actually count as this attack's dmg

                var heal = outputDmg * attributes.LifeSteal;
                if (heal > 0) {
                    attributes.Heal(heal);
                }

                attributes.RegenEnergy(hero.Trait.energyRegenPerAttack);

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
                // why subtract 1 from stack???
                // 1 stack is already applied in TakeDamage
            }

            atkExecuted++;
        }
    }
}