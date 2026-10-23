using System.Collections;
using RExt.Utils;

namespace AutoChess.Game {
    public class Mecanim_Irelia : Mecanim {
        public override void UseSkill(int variant) {
            PlaySkillRoutine(DoUseSkill());
        }

        IEnumerator DoUseSkill() {
            Interact(Interaction.Skill, (paramSkill, 0));
            yield return BetterWaitForSeconds.Wait(2.35f);
            bodyParts.SetBodyParts(0, ("dragon_0", true), ("dragon_1", true));
            yield return BetterWaitForSeconds.Wait(2.13f);
            bodyParts.SetBodyParts(0, ("dragon_0", false), ("dragon_1", false));
        }

        protected override void OnSkillInterrupted() {
            bodyParts.SetBodyParts(0, ("dragon_0", false), ("dragon_1", false));
        }
    }
}
