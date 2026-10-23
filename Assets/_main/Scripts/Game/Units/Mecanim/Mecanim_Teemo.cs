using System.Collections;
using RExt.Utils;

namespace AutoChess.Game {
    public class Mecanim_Teemo : Mecanim {
        public override void UseSkill(int variant) {
            PlaySkillRoutine(DoUseSkill());
        }

        IEnumerator DoUseSkill() {
            Interact(Interaction.Skill, (paramSkill, 0));
            bodyParts.SetBodyParts(0, ("bomb_0", true), ("bomb_1", true), ("bomb_2", true));
            yield return BetterWaitForSeconds.Wait(1.56f);
            bodyParts.SetBodyParts(0, ("bomb_2", false));
            yield return BetterWaitForSeconds.Wait(1.17f);
            bodyParts.SetBodyParts(0, ("bomb_0", false));
            yield return BetterWaitForSeconds.Wait(0.56f);
            bodyParts.SetBodyParts(0, ("bomb_1", false));
        }

        protected override void OnSkillInterrupted() {
            bodyParts.SetBodyParts(0, ("bomb_0", false), ("bomb_1", false), ("bomb_2", false));
        }
    }
}
