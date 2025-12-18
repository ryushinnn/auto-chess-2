using System;
using System.Collections;
using RExt.Utils;
using UnityEngine;

public class Mecanim_Malphite : Mecanim {
    Coroutine skillCoroutine;
    
    public override void UseSkill() {
        if (skillCoroutine != null) {
            StopCoroutine(skillCoroutine);
        }
        skillCoroutine = StartCoroutine(DoUseSkill());
    }
    
    IEnumerator DoUseSkill() {
        Interact(Interaction.Skill, (paramSkill, 0));
        yield return BetterWaitForSeconds.Wait(0.46f);
        bodyParts.SetBodyParts(0,("punch",true));
        yield return BetterWaitForSeconds.Wait(3.68f);
        bodyParts.SetBodyParts(0,("punch",false));
    }
    
    public override void InterruptSkill() {
        DoNothing();
        if (useSkillCoroutine != null) {
            StopCoroutine(useSkillCoroutine);
            bodyParts.SetBodyParts(0, ("punch", false));
        }
    }
}