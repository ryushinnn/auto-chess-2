using UnityEngine;

public class Mecanim_Yone : Mecanim {
    Hero hero;
    
    protected override void SetUp() {
        hero = GetComponentInParent<Hero>();
        animator.SetBool(paramAttackIn, true);
        animator.SetBool(paramAttackOut, true);
    }

    public override void UseSkill() {
        var atkProcessor = (AttackProcessor_Yone)hero.GetAbility<HeroAttack>().Processor;
        if (atkProcessor.CurrentSword == YoneSword.Divine) {
            Interact(Interaction.Skill, (paramSkill, 0));
        }
        else if (atkProcessor.CurrentSword == YoneSword.Devil) {
            Interact(Interaction.Skill, (paramSkill, 1));
        }
    }

    protected override void ModifyBodyParts() {
        switch (currentState) {
            case State.Death:
                bodyParts.SetBodyParts(0,
                    ("mouth", false),
                    ("r_hand",false),
                    ("eyes",false),
                    ("decor",false),
                    ("body",false),
                    ("horns",false),
                    ("hair",false));
                break;
        }
    }
}