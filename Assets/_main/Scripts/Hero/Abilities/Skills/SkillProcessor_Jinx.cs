using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RExt.Extensions;
using Random = UnityEngine.Random;

public class SkillProcessor_Jinx : SkillProcessor {
    readonly int rockets;
    readonly float interval;
    readonly float baseDmgPerRocket;
    readonly float dmgMulPerRocket;

    List<Hero> affectedTargets = new();
    
    public SkillProcessor_Jinx(BattleHero hero) : base(hero) {
        animationLength = 4.2f;
        timers = new[] { 2f };
        
        var skillParams = hero.Trait.skillParams;
        rockets = (int)skillParams[0].value;
        interval = (int)skillParams[1].value;
        baseDmgPerRocket = skillParams[2].value;
        dmgMulPerRocket = skillParams[3].value;
    }

    public override void Process(float timer) {
        if (timer >= timers[0] && skillExecuted == 0) {
            ShotRockets();
            skillExecuted++;
        }
    }

    async void ShotRockets() {
        affectedTargets.Clear();
        ShotRocket();
        for (int i = 1; i < rockets; i++) {
            await Task.Delay(interval.ToMilliseconds());
            ShotRocket();
        }
    }

    void ShotRocket() {
        if (hero.Target == null) return;

        var type = Random.Range(0, 3);
        Damage dmg = null;
        switch (type) {
            case 0:
                dmg = attributes.GetDamage(DamageType.Physical, attributes.Crit(),
                    scaledValues: new[] { (dmgMulPerRocket, DamageType.Physical) },
                    fixedValues: new[] { baseDmgPerRocket });
                break;
            
            case 1:
                dmg = attributes.GetDamage(DamageType.Magical, attributes.Crit(),
                    scaledValues: new[] { (dmgMulPerRocket, DamageType.Physical) },
                    fixedValues: new[] { baseDmgPerRocket });
                break;
            
            case 2:
                dmg = attributes.GetDamage(DamageType.True, attributes.Crit(),
                    scaledValues: new[] { (dmgMulPerRocket, DamageType.Physical) },
                    fixedValues: new[] { baseDmgPerRocket });
                break;
        }

        var isNewTarget = !affectedTargets.Contains(hero.Target);
        
        hero.Target.GetAbility<HeroAttributes>().TakeDamage(dmg, isNewTarget);

        if (isNewTarget) {
            affectedTargets.Add(hero.Target);
        }
    }
}