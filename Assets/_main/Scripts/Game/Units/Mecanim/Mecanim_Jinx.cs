using System.Collections;
using RExt.Utils;

namespace AutoChess.Game {
    public class Mecanim_Jinx : Mecanim {
        public override void UseSkill(int variant) {
            PlaySkillRoutine(DoUseSkill());
        }

        IEnumerator DoUseSkill() {
            bodyParts.SetBodyParts(0, ("rocket", true));
            Interact(Interaction.Skill, (paramSkill, 0));
            yield return BetterWaitForSeconds.Wait(4f);
            bodyParts.SetBodyParts(0, ("rocket", false));
        }

        protected override void OnSkillInterrupted() {
            bodyParts.SetBodyParts(0, ("rocket", false));
        }
    }
}
