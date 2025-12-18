public class SelectorNode : CompositeNode {
    public override NodeState Evaluate() {
        foreach (var child in children) {
            switch (child.Evaluate()) {
                case NodeState.Running:
                    State = NodeState.Running;
                    return State;
                case NodeState.Success:
                    State = NodeState.Success;
                    return State;
            }
        }
        
        State = NodeState.Failure;
        return State;
    }
}