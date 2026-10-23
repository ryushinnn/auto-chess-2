using System.Collections;
using RExt.Utils;

namespace AutoChess.Game {
    public class Mecanim_Aatrox : Mecanim {
        protected override void ModifyBodyParts() {
            switch (currentState) {
                case State.Idle:
                case State.Run:
                    bodyParts.SetBodyParts(0, ("spikes", false), ("wings_0", true), ("wings_1", true));
                    break;

                case State.Death:
                    bodyParts.SetBodyParts(0, ("spikes", true), ("wings_0", false), ("wings_1", false));
                    break;
            }
        }

        public override void UseSkill(int variant) {
            PlaySkillRoutine(DoUseSkill());
        }

        IEnumerator DoUseSkill() {
            Interact(Interaction.Skill, (paramSkill, 0));
            yield return BetterWaitForSeconds.Wait(1.2f);
            Interact(Interaction.Skill, (paramSkill, 1));
            yield return BetterWaitForSeconds.Wait(1.9f);
            Interact(Interaction.Skill, (paramSkill, 2));
        }
    }
}
