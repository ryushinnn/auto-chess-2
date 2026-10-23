using System;
using System.Collections.Generic;

namespace AutoChess.Core {
    /// <summary>
    /// A* on a <see cref="HexGrid"/> with a binary heap and reusable buffers (one instance per battle, no
    /// allocation per query besides growing the output list).
    /// </summary>
    public sealed class PathFinder {
        readonly HexGrid grid;
        readonly int[] gScore;
        readonly int[] cameFrom;
        readonly int[] visitStamp;
        readonly int[] closedStamp;
        readonly int[] heapCell;
        readonly int[] heapKey;
        int heapCount;
        int stamp;

        public PathFinder(HexGrid grid) {
            this.grid = grid;
            var n = grid.CellCount;
            gScore = new int[n];
            cameFrom = new int[n];
            visitStamp = new int[n];
            closedStamp = new int[n];
            heapCell = new int[n * 7 + 1];
            heapKey = new int[n * 7 + 1];
        }

        /// <summary>
        /// Shortest path from <paramref name="start"/> to <paramref name="goal"/> (both included). Cells for which
        /// <paramref name="blocked"/> returns true are avoided, except the start and the goal. Returns false
        /// (and an empty list) when the goal is unreachable.
        /// </summary>
        public bool FindPath(int start, int goal, List<int> result, Func<int, bool> blocked = null) {
            result.Clear();
            if (start == goal) {
                result.Add(start);
                return true;
            }

            stamp++;
            heapCount = 0;
            visitStamp[start] = stamp;
            gScore[start] = 0;
            cameFrom[start] = -1;
            Push(start, grid.Distance(start, goal));

            while (heapCount > 0) {
                var current = Pop();
                if (closedStamp[current] == stamp) continue;
                closedStamp[current] = stamp;
                if (current == goal) {
                    for (var c = goal; c != -1; c = cameFrom[c]) result.Add(c);
                    result.Reverse();
                    return true;
                }

                foreach (var next in grid.NeighborsOf(current)) {
                    if (closedStamp[next] == stamp) continue;
                    if (next != goal && blocked != null && blocked(next)) continue;
                    var g = gScore[current] + 1;
                    if (visitStamp[next] == stamp && g >= gScore[next]) continue;
                    visitStamp[next] = stamp;
                    gScore[next] = g;
                    cameFrom[next] = current;
                    Push(next, g + grid.Distance(next, goal));
                }
            }
            return false;
        }

        // Binary min-heap of (cell, f). Ties are broken by cell index so equal-length paths are always the same.
        void Push(int cell, int key) {
            var i = heapCount++;
            heapCell[i] = cell;
            heapKey[i] = key;
            while (i > 0) {
                var parent = (i - 1) >> 1;
                if (!Less(i, parent)) break;
                Swap(i, parent);
                i = parent;
            }
        }

        int Pop() {
            var top = heapCell[0];
            heapCount--;
            heapCell[0] = heapCell[heapCount];
            heapKey[0] = heapKey[heapCount];
            var i = 0;
            while (true) {
                var l = i * 2 + 1;
                if (l >= heapCount) break;
                var r = l + 1;
                var m = r < heapCount && Less(r, l) ? r : l;
                if (!Less(m, i)) break;
                Swap(i, m);
                i = m;
            }
            return top;
        }

        bool Less(int a, int b) => heapKey[a] != heapKey[b] ? heapKey[a] < heapKey[b] : heapCell[a] < heapCell[b];

        void Swap(int a, int b) {
            (heapCell[a], heapCell[b]) = (heapCell[b], heapCell[a]);
            (heapKey[a], heapKey[b]) = (heapKey[b], heapKey[a]);
        }
    }
}
