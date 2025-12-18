using System;
using RExt.Patterns.Singleton;
using UnityEngine;

public class Deck : Singleton<Deck> {
    [SerializeField] Transform nodeParent;
    [SerializeField] float nodeWidth;
    [SerializeField] float nodeHeight;

    DeckNode[] nodes;
    
    const int SIZE = 9;
    const float LIMIT_X_LEFT = -8.4f;
    const float LIMIT_X_RIGHT = 7.1f;
    const float LIMIT_Z_UP = -6.6f;
    
    public void SpawnNodes(Action<DeckNode[], float, float> onComplete) {
        nodes = new DeckNode[SIZE];
        for (int i = 0; i < SIZE; i++) {
            var worldPos = new Vector3((i - SIZE / 2) * nodeWidth, 0, 0) + nodeParent.position;
            var node = new DeckNode(i, worldPos);
            nodes[i] = node;
            var nodeVisual = new GameObject($"[{i}]");
            nodeVisual.transform.SetParent(nodeParent);
            nodeVisual.transform.position = node.WorldPosition;
        }
        
        onComplete?.Invoke(nodes, nodeWidth, nodeHeight);
    }

    public DeckNode GetNearestNode(Vector3 position, Func<int, bool> condition = null) {
        if (position.x < LIMIT_X_LEFT || position.x > LIMIT_X_RIGHT || position.z > LIMIT_Z_UP) {
            return null;
        }

        var minDist = Mathf.Infinity;
        DeckNode node = null;
        for (int i = 0; i < SIZE; i++) {
            var dist = Vector3.Distance(nodes[i].WorldPosition, position);
            if (dist < minDist && (condition == null || condition(i))) {
                minDist = dist;
                node = nodes[i];
            }
        }

        return node;
    }

    public DeckNode GetLowestAvailableNode() {
        for (int i = 0; i < SIZE; i++) {
            if (nodes[i].IsEmpty()) {
                return nodes[i];
            }
        }

        return null;
    }
}