using System;
using System.Collections;
using RExt.Utils;
using UnityEngine;

public class Mecanim_Caitlyn : Mecanim {
    Coroutine skillCoroutine;
    
    public override void UseSkill() {
        if (skillCoroutine != null) {
            StopCoroutine(skillCoroutine);
        }
        skillCoroutine = StartCoroutine(DoUseSkill());
    }

    IEnumerator DoUseSkill() {
        Interact(Interaction.Skill, (paramSkill, 0));
        yield return BetterWaitForSeconds.Wait(1f);
        bodyParts.SetBodyParts(0,("cake",true));
        yield return BetterWaitForSeconds.Wait(3.1f);
        bodyParts.SetBodyParts(0,("cake",false));
    }
}