using System.Collections;
using RExt.Utils;

namespace AutoChess.Game {
    public class Mecanim_Morgana : Mecanim {
        public override void UseSkill(int variant) {
            PlaySkillRoutine(DoUseSkill());
        }

        IEnumerator DoUseSkill() {
            Interact(Interaction.Skill, (paramSkill, 0));
            yield return BetterWaitForSeconds.Wait(0.3f);
            bodyParts.SetBodyParts(0, ("wings_0", true), ("wings_1", true));
            yield return BetterWaitForSeconds.Wait(0.7f);
            bodyParts.SetBodyParts(0, ("wings_0", false), ("wings_1", false));
        }

        protected override void OnSkillInterrupted() {
            bodyParts.SetBodyParts(0, ("wings_0", false), ("wings_1", false));
        }
    }
}
