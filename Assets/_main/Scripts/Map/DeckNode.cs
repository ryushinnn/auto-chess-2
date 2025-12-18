using System;
using UnityEngine;

public class DeckNode : Node {
    public int LinePosition { get; private set; }

    public DeckNode(int index, Vector3 position) : base(position) {
        LinePosition = index;
    }
    
    public override string ToString() {
        return $"D({LinePosition})";
    }
}