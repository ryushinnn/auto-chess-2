using System;
using System.Collections.Generic;
using UnityEngine;

public enum Direction {
    TopLeft,
    TopRight,
    Left,
    Right,
    BotLeft,
    BotRight
}

public static class DirectionUtils {
    static readonly Dictionary<Direction, Vector2Int> EVEN_ROW_OFFSETS = new() {
        { Direction.TopLeft, new Vector2Int(-1, 0) },
        { Direction.TopRight, new Vector2Int(-1, 1) },
        { Direction.Left, new Vector2Int(0, -1) },
        { Direction.Right, new Vector2Int(0, 1) },
        { Direction.BotLeft, new Vector2Int(1, 0) },
        { Direction.BotRight, new Vector2Int(1, 1) },
    };
    
    static readonly Dictionary<Direction, Vector2Int> ODD_ROW_OFFSETS = new() {
        { Direction.TopLeft, new Vector2Int(-1, -1) },
        { Direction.TopRight, new Vector2Int(-1, 0) },
        { Direction.Left, new Vector2Int(0, -1) },
        { Direction.Right, new Vector2Int(0, 1) },
        { Direction.BotLeft, new Vector2Int(1, -1) },
        { Direction.BotRight, new Vector2Int(1, 0) },
    };

    public static Vector2Int GetOffset(Direction direction, int row) {
        return (row % 2 == 0) ? EVEN_ROW_OFFSETS[direction] : ODD_ROW_OFFSETS[direction];
    }
    
    public static Direction[] GetAllDirections() {
        return Enum.GetValues(typeof(Direction)) as Direction[];
    }
}