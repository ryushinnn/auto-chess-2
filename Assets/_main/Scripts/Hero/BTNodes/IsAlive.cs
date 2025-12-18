public class IsAlive : BTNode {
    BattleHero hero;
    
    public IsAlive(BattleHero hero) {
        this.hero = hero;
    }
    
    public override NodeState Evaluate() {
        State = hero.GetAbility<HeroAttributes>().IsAlive ? NodeState.Success : NodeState.Failure;
        return State;
    }
}