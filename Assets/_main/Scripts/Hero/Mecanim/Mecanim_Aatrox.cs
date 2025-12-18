using System;
using System.Collections;
using RExt.Utils;
using UnityEngine;

public class Mecanim_Aatrox : Mecanim {
    Coroutine skillCoroutine;
    
    protected override void ModifyBodyParts() {
        switch (currentState) {
            case State.Idle:
                bodyParts.SetBodyParts(0, ("spikes", false), ("wings_0", true), ("wings_1", true));
                break;
            
            case State.Run:
                bodyParts.SetBodyParts(0, ("spikes", false), ("wings_0", true), ("wings_1", true));
                break;
            
            case State.Death:
                bodyParts.SetBodyParts(0, ("spikes", true), ("wings_0", false), ("wings_1", false));
                break;
        }
    }

    public override void UseSkill() {
        if (skillCoroutine != null) {
            StopCoroutine(skillCoroutine);
        }
        skillCoroutine = StartCoroutine(DoUseSkill());
    }

    IEnumerator DoUseSkill() {
        Interact(Interaction.Skill, (paramSkill, 0));
        yield return BetterWaitForSeconds.Wait(1.2f);
        Interact(Interaction.Skill, (paramSkill, 1));
        yield return BetterWaitForSeconds.Wait(1.9f);
        Interact(Interaction.Skill, (paramSkill, 2));
    }
}