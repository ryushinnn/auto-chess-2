public class Parallel : CompositeNode {
    bool succeedOnAll;

    public Parallel(bool succeedOnAll) {
        this.succeedOnAll = succeedOnAll;
    }

    public override NodeState Evaluate() {
        bool anyChildRunning = false;

        foreach (var child in children) {
            switch (child.Evaluate()) {
                case NodeState.Running:
                    anyChildRunning = true;
                    break;
                case NodeState.Failure:
                    if (succeedOnAll) {
                        State = NodeState.Failure;
                        return State;
                    }

                    break;
                case NodeState.Success:
                    if (!succeedOnAll) {
                        State = NodeState.Success;
                        return State;
                    }

                    break;
            }
        }

        State = succeedOnAll ? NodeState.Success : (anyChildRunning ? NodeState.Running : NodeState.Failure);
        return State;
    }
}