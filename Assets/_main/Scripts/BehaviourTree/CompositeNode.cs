using System.Collections.Generic;

public abstract class CompositeNode : BTNode {
    protected List<BTNode> children = new();

    public abstract override NodeState Evaluate();

    public void AddChild(BTNode child) {
        children.Add(child);
    }
}