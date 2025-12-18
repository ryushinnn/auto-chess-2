public abstract class BTNode {
    public enum NodeState {
        Running,
        Success,
        Failure
    }
        
    public NodeState State { get; protected set; }
        
    public abstract NodeState Evaluate();
}