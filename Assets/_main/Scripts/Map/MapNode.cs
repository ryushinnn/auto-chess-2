using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MapNode : Node {
    public int X => GridPosition.x;
    public int Y => GridPosition.y;
    public Vector2Int GridPosition { get; private set; }
    
    public MapNode(int x, int y, Vector3 position) : base(position) {
        GridPosition = new Vector2Int(x, y);
    }

    public override string ToString() {
        return $"M({X},{Y})";
    }
}