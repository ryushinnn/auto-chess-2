using System;

public class Repeater : BTNode {
    BTNode child;
    int repeatCount;
    int currentCount;
    Func<bool> condition;

    public Repeater(BTNode child, int repeatCount) {
        this.child = child;
        this.repeatCount = repeatCount;
        this.condition = null;
    }

    public Repeater(BTNode child, Func<bool> condition) {
        this.child = child;
        this.repeatCount = -1;
        this.condition = condition;
    }

    public override NodeState Evaluate() {
        if (repeatCount > 0 && currentCount >= repeatCount) {
            State = NodeState.Success;
            return State;
        }

        if (condition != null && condition()) {
            State = NodeState.Success;
            return State;
        }

        var childState = child.Evaluate();
        if (childState == NodeState.Success || childState == NodeState.Failure) {
            currentCount++;
        }

        State = NodeState.Running;
        return State;
    }
}