using System.Collections;
using RExt.Utils;

namespace AutoChess.Game {
    public class Mecanim_Malphite : Mecanim {
        public override void UseSkill(int variant) {
            PlaySkillRoutine(DoUseSkill());
        }

        IEnumerator DoUseSkill() {
            Interact(Interaction.Skill, (paramSkill, 0));
            yield return BetterWaitForSeconds.Wait(0.46f);
            bodyParts.SetBodyParts(0, ("punch", true));
            yield return BetterWaitForSeconds.Wait(3.68f);
            bodyParts.SetBodyParts(0, ("punch", false));
        }

        protected override void OnSkillInterrupted() {
            bodyParts.SetBodyParts(0, ("punch", false));
        }
    }
}
