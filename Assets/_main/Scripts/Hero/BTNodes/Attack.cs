using UnityEngine;

public class Attack : BTNode {
    BattleHero hero;
    
    public Attack(BattleHero hero) {
        this.hero = hero;
    }

    public override NodeState Evaluate() {
        State = hero.GetAbility<HeroAttack>().Attack() ? NodeState.Success : NodeState.Failure;
        return State;
    }
}