using System.Collections;
using RExt.Utils;

namespace AutoChess.Game {
    public class Mecanim_Katarina : Mecanim {
        public override void UseSkill(int variant) {
            PlaySkillRoutine(DoUseSkill());
        }

        IEnumerator DoUseSkill() {
            Interact(Interaction.Skill);
            yield return BetterWaitForSeconds.Wait(1);
            Interact(Interaction.Skill);
            yield return BetterWaitForSeconds.Wait(1);
            Interact(Interaction.Skill);
        }
    }
}
