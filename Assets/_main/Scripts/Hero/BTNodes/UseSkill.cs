using UnityEngine;

public class UseSkill : BTNode {
    BattleHero hero;
    
    public UseSkill(BattleHero hero) {
        this.hero = hero;
    }
    
    public override NodeState Evaluate() {
        State = hero.GetAbility<HeroSkill>().UseSkill() ? NodeState.Success : NodeState.Failure;
        return State;
    }
}