public class Inverter : BTNode {
    BTNode child;

    public Inverter(BTNode child) {
        this.child = child;
    }

    public override NodeState Evaluate() {
        switch (child.Evaluate()) {
            case NodeState.Success:
                State = NodeState.Failure;
                return State;
            case NodeState.Failure:
                State = NodeState.Success;
                return State;
            default:
                State = NodeState.Running;
                return State;
        }
    }
}