using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RExt.Extensions;

public class SkillProcessor_Katarina : SkillProcessor {
    readonly int hits;
    readonly float interval;
    readonly float baseDmgPerHit;
    readonly float dmgMulPerHit;
    readonly float antiHealDuration;
    
    readonly List<Hero> affectedTargets = new();

    public SkillProcessor_Katarina(BattleHero hero) : base(hero) {
        animationLength = 3.1f;
        
        var skillParams = hero.Trait.skillParams;
        hits = (int)skillParams[0].value;
        interval = skillParams[1].value;
        baseDmgPerHit = skillParams[2].value;
        dmgMulPerHit = skillParams[3].value;
        antiHealDuration = skillParams[4].value;
    }

    public override void Process(float timer) {
        if (skillExecuted == 0) {
            TurnAround();
            skillExecuted++;
        }
    }

    async void TurnAround() {
        affectedTargets.Clear();
        Cut();
        for (int i=1; i<hits; i++) {
            await Task.Delay(interval.ToMilliseconds());
            Cut();
        }
    }

    void Cut() {
        if (hero.Target == null) return;

        var dmg = attributes.GetDamage(DamageType.Magical, attributes.Crit(),
            scaledValues: new[] { (dmgMulPerHit, DamageType.Physical) },
            fixedValues: new[] { baseDmgPerHit });
        var isNewTarget = !affectedTargets.Contains(hero.Target);
        
        hero.Target.GetAbility<HeroAttributes>().TakeDamage(dmg,isNewTarget);

        if (isNewTarget) {
            hero.Target.GetAbility<HeroStatusEffects>().AntiHeal(antiHealDuration);
            affectedTargets.Add(hero.Target);
        }
    }
}