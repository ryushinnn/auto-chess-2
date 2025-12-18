using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using RExt.Extensions;
using UnityEngine;
using Random = UnityEngine.Random;

public class SkillProcessor_Ashe : SkillProcessor {
    readonly int arrowSetMin;
    readonly int arrowSetMax;
    readonly float baseDmg;
    readonly float pDmgMul;
    readonly float mDmgMul;
    readonly float totalTime;
    readonly float radius;
    
    public SkillProcessor_Ashe(BattleHero hero) : base(hero) {
        animationLength = 3;
        timers = new[] { 0.75f };

        var skillParams = hero.Trait.skillParams;
        arrowSetMin = (int)skillParams[0].value;
        arrowSetMax = (int)skillParams[1].value;
        baseDmg = skillParams[2].value;
        pDmgMul = skillParams[3].value;
        mDmgMul = skillParams[4].value;
        totalTime = skillParams[5].value;
        radius = skillParams[6].value;
    }

    public override void Process(float timer) {
        if (timer >= timers[0] && skillExecuted == 0) {
            ShotArrows();
            skillExecuted++;
        }
    }

    void ShotArrows() {
        var dotArea = GameObject.Instantiate(PrefabDB.Instance.DotArea);
        dotArea.transform.position = hero.Target.WorldPosition;
        var dmg = attributes.GetDamage(DamageType.Physical, false,
            scaledValues: new[] {
                (pDmgMul, DamageType.Physical),
                (mDmgMul, DamageType.Magical)
            }, fixedValues: new[] { baseDmg });
        var set = Random.Range(arrowSetMin, arrowSetMax + 1);
        var vfx = VfxPool.Instance.GetVfx("ashe_skill");
        dotArea.SetData(hero, dmg, set, totalTime / set, true, radius, vfx);
    }
}