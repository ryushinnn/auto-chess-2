using System;
using System.Collections;
using RExt.Utils;
using UnityEngine;

public class Mecanim_Teemo : Mecanim {
    Coroutine skillCoroutine;
    
    public override void UseSkill() {
        if (skillCoroutine != null) {
            StopCoroutine(skillCoroutine);
        }
        skillCoroutine = StartCoroutine(DoUseSkill());
    }
    
    IEnumerator DoUseSkill() {
        Interact(Interaction.Skill, (paramSkill, 0));
        bodyParts.SetBodyParts(0, ("bomb_0",true),("bomb_1",true),("bomb_2",true));
        yield return BetterWaitForSeconds.Wait(1.56f);
        bodyParts.SetBodyParts(0,("bomb_2",false));
        yield return BetterWaitForSeconds.Wait(1.17f);
        bodyParts.SetBodyParts(0,("bomb_0",false));
        yield return BetterWaitForSeconds.Wait(0.56f);
        bodyParts.SetBodyParts(0, ("bomb_1",false));
    }
    
    public override void InterruptSkill() {
        DoNothing();
        if (useSkillCoroutine != null) {
            StopCoroutine(useSkillCoroutine);
            bodyParts.SetBodyParts(0, ("bomb_0",false),("bomb_1",false),("bomb_2",false));
        }
    }
}