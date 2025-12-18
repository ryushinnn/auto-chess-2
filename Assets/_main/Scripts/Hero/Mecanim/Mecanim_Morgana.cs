using System.Collections;
using RExt.Utils;
using UnityEngine;

public class Mecanim_Morgana : Mecanim {
    Coroutine skillCoroutine;
    
    public override void UseSkill() {
        if (skillCoroutine != null) {
            StopCoroutine(skillCoroutine);
        }
        skillCoroutine = StartCoroutine(DoUseSkill());
    }
    
    IEnumerator DoUseSkill() {
        Interact(Interaction.Skill, (paramSkill, 0));
        yield return BetterWaitForSeconds.Wait(0.3f);
        bodyParts.SetBodyParts(0,("wings_0",true));
        bodyParts.SetBodyParts(0,("wings_1",true));
        yield return BetterWaitForSeconds.Wait(0.7f);
        bodyParts.SetBodyParts(0,("wings_0",false));
        bodyParts.SetBodyParts(0,("wings_1",false));
    }
    
    public override void InterruptSkill() {
        DoNothing();
        if (useSkillCoroutine != null) {
            StopCoroutine(useSkillCoroutine);
            bodyParts.SetBodyParts(0, ("wings_0", false));
            bodyParts.SetBodyParts(0, ("wings_1", false));
        }
    }
}