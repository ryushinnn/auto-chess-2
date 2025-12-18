using System;
using System.Collections;
using RExt.Utils;
using UnityEngine;

public class Mecanim_Irelia : Mecanim {
    Coroutine skillCoroutine;
    
    public override void UseSkill() {
        if (skillCoroutine != null) {
            StopCoroutine(skillCoroutine);
        }
        skillCoroutine = StartCoroutine(DoUseSkill());
    }

    IEnumerator DoUseSkill() {
        Interact(Interaction.Skill, (paramSkill, 0));
        yield return BetterWaitForSeconds.Wait(2.35f);
        bodyParts.SetBodyParts(0,("dragon_0",true),("dragon_1",true));
        yield return BetterWaitForSeconds.Wait(2.13f);
        bodyParts.SetBodyParts(0,("dragon_0",false),("dragon_1",false));
    }
    
    public override void InterruptSkill() {
        DoNothing();
        if (useSkillCoroutine != null) {
            StopCoroutine(useSkillCoroutine);
            bodyParts.SetBodyParts(0,("dragon_0",false),("dragon_1",false));
        }
    }
}