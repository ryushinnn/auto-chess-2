public class SequenceNode : CompositeNode {
    public override NodeState Evaluate() {
        foreach (var child in children) {
            switch (child.Evaluate()) {
                case NodeState.Running:
                    State = NodeState.Running;
                    return State;
                case NodeState.Failure:
                    State = NodeState.Failure;
                    return State;
            }
        }
        
        State = NodeState.Success;
        return State;
    }
}