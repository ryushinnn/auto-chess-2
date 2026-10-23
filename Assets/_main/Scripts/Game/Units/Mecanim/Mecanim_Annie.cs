using System.Collections;
using RExt.Utils;

namespace AutoChess.Game {
    public class Mecanim_Annie : Mecanim {
        public override void UseSkill(int variant) {
            PlaySkillRoutine(DoUseSkill());
        }

        IEnumerator DoUseSkill() {
            Interact(Interaction.Skill, (paramSkill, 0));
            bodyParts.SetBodyParts(0, ("tibbers", true), ("tibbers_hand", true));
            yield return BetterWaitForSeconds.Wait(1.6f);
            bodyParts.SetBodyParts(0, ("tibbers", false), ("tibbers_hand", false));
        }

        protected override void OnSkillInterrupted() {
            bodyParts.SetBodyParts(0, ("tibbers", false), ("tibbers_hand", false));
        }
    }
}
