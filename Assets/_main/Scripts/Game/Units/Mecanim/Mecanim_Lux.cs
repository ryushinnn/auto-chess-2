using System.Collections;
using RExt.Utils;

namespace AutoChess.Game {
    public class Mecanim_Lux : Mecanim {
        // the skill clip (Cast_Cycle) is a 0.87 s staff channel, held over the 2 s of the beam
        public override void UseSkill(int variant) {
            PlaySkillRoutine(DoUseSkill());
        }

        IEnumerator DoUseSkill() {
            for (var i = 0; i < 2; i++) {
                Interact(Interaction.Skill, (paramSkill, 0));
                yield return BetterWaitForSeconds.Wait(0.86f);
            }
        }
    }
}
