using System.Collections;
using RExt.Utils;
using UnityEngine;

public class Mecanim_Katarina : Mecanim {
    Coroutine skillCoroutine;

    public override void UseSkill() {
        if (skillCoroutine != null) {
            StopCoroutine(skillCoroutine);
        }
        skillCoroutine = StartCoroutine(DoUseSkill());
    }

    IEnumerator DoUseSkill() {
        Interact(Interaction.Skill);
        yield return BetterWaitForSeconds.Wait(1);
        Interact(Interaction.Skill);
        yield return BetterWaitForSeconds.Wait(1);
        Interact(Interaction.Skill);
    }
}