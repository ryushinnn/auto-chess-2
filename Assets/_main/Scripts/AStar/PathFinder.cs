using System.Collections.Generic;
using UnityEngine;

public static class PathFinder {
    public static List<MapNode> FindPath(this Map map, MapNode start, MapNode goal) {
        var openSet = new PriorityQueue<MapNode>();
        var cameFrom = new Dictionary<MapNode, MapNode>();
        var gScore = new Dictionary<MapNode, int>();
        var fScore = new Dictionary<MapNode, int>();

        openSet.Enqueue(start, 0);
        gScore[start] = 0;
        fScore[start] = Heuristic(start, goal);

        while (openSet.Count > 0) {
            var current = openSet.Dequeue();

            if (current == goal) {
                return ReconstructPath(cameFrom, current);
            }

            foreach (var neighbor in map.GetNeighbors(current,1)) {
                int tentativeG = gScore[current] + 1;
                if (!gScore.ContainsKey(neighbor) || tentativeG < gScore[neighbor]) {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + Heuristic(neighbor, goal);

                    if (!openSet.Contains(neighbor)) {
                        openSet.Enqueue(neighbor, fScore[neighbor]);
                    }
                }
            }
        }

        return new List<MapNode>();
    }

    static int Heuristic(MapNode a, MapNode b) {
        var dx = Mathf.Abs(a.X - b.X);
        var dy = Mathf.Abs(a.Y - b.Y);
        return Mathf.Max(dx, dy);
    }

    static List<MapNode> ReconstructPath(Dictionary<MapNode, MapNode> cameFrom, MapNode current) {
        var path = new List<MapNode> { current };
        while (cameFrom.ContainsKey(current)) {
            current = cameFrom[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }
}