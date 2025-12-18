public class FindTarget : BTNode {
    BattleHero hero;
    
    public FindTarget(BattleHero hero) {
        this.hero = hero;
    }
    
    public override NodeState Evaluate() {
        hero.FindTarget();
        State = NodeState.Success;
        return State;
    }
}