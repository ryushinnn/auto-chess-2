using System.Collections;
using RExt.Utils;

namespace AutoChess.Game {
    public class Mecanim_Blitzcrank : Mecanim {
        // skill 0 = the rocket fist thrown out (grab at 0.5 s), skill 1 = the punch once the target is pulled in (0.9 s)
        public override void UseSkill(int variant) {
            PlaySkillRoutine(DoUseSkill());
        }

        IEnumerator DoUseSkill() {
            Interact(Interaction.Skill, (paramSkill, 0));
            yield return BetterWaitForSeconds.Wait(0.55f);
            Interact(Interaction.Skill, (paramSkill, 1));
        }
    }
}
