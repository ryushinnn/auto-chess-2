using System.Collections;
using RExt.Utils;

namespace AutoChess.Game {
    public class Mecanim_Gwen : Mecanim {
        // the skill clip (Cast_Cycle) is a 0.8 s scissor twirl, replayed over the ~2.7 s of snips
        public override void UseSkill(int variant) {
            PlaySkillRoutine(DoUseSkill());
        }

        IEnumerator DoUseSkill() {
            for (var i = 0; i < 3; i++) {
                Interact(Interaction.Skill, (paramSkill, 0));
                yield return BetterWaitForSeconds.Wait(0.8f);
            }
        }
    }
}
