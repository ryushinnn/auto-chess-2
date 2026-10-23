namespace AutoChess.Game {
    public class Mecanim_Yone : Mecanim {
        protected override void SetUp() {
            animator.SetBool(paramAttackIn, true);
            animator.SetBool(paramAttackOut, true);
        }

        /// <param name="variant">0 = divine sword, 1 = devil sword (Core.Combat.Heroes.YoneSword).</param>
        public override void UseSkill(int variant) {
            Interact(Interaction.Skill, (paramSkill, variant == 0 ? 0 : 1));
        }

        protected override void ModifyBodyParts() {
            if (currentState != State.Death) return;
            bodyParts.SetBodyParts(0,
                ("mouth", false),
                ("r_hand", false),
                ("eyes", false),
                ("decor", false),
                ("body", false),
                ("horns", false),
                ("hair", false));
        }
    }
}
