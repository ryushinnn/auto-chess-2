using UnityEngine;

public class OnDeathBehaviour : StateMachineBehaviour {
    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {
        animator.enabled = false;
    }
}