using System;
using System.Collections;
using RExt.Utils;
using UnityEngine;

public class Mecanim_Jinx : Mecanim {
    Coroutine skillCoroutine;
    
    public override void UseSkill() {
        if (skillCoroutine != null) {
            StopCoroutine(skillCoroutine);
        }
        skillCoroutine = StartCoroutine(DoUseSkill());
    }
    
    IEnumerator DoUseSkill() {
        bodyParts.SetBodyParts(0, ("rocket",true));
        Interact(Interaction.Skill, (paramSkill, 0));
        yield return BetterWaitForSeconds.Wait(4f);
        bodyParts.SetBodyParts(0, ("rocket",false));
    }    
    
    public override void InterruptSkill() {
        DoNothing();
        if (useSkillCoroutine != null) {
            StopCoroutine(useSkillCoroutine);
            bodyParts.SetBodyParts(0, ("rocket",false));
        }
    }
}