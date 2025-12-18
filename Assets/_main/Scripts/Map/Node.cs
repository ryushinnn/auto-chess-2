using UnityEngine;

public enum NodeState {
    Empty,
    Targeted,
    Occupied,
}

public abstract class Node {
    public Vector3 WorldPosition { get; protected set; }
    public NodeState State { get; private set; }

    protected Node(Vector3 worldPosition) {
        WorldPosition = worldPosition;
        State = NodeState.Empty;
    }

    public bool IsEmpty() {
        return State == NodeState.Empty;
    }

    public virtual void SetToEmpty() {
        ChangeState(NodeState.Empty);
    }
    
    public virtual void ChangeState(NodeState state) {
        State = state;
    }
}