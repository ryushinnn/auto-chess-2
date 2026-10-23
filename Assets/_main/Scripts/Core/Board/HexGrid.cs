using System;
using System.Collections.Generic;

namespace AutoChess.Core {
    /// <summary>
    /// Static shape of the battle board: cell centers on the battle plane, neighbour tables and nearest-cell
    /// queries. Shared by every battle of a match; holds no occupancy state.
    /// </summary>
    public sealed class HexGrid {
        public readonly int Rows;
        public readonly int Cols;
        public readonly float CellWidth;
        public readonly float CellHeight;
        /// <summary>Distance used to convert movement speed (cells per second) into plane units.</summary>
        public readonly float AverageCellDistance;

        readonly Vec2[] centers;
        readonly int[][] neighbors;

        public int CellCount => Rows * Cols;

        public HexGrid(int rows, int cols, float cellWidth, float cellHeight) {
            if (rows <= 0 || cols <= 0) throw new ArgumentException("grid size must be positive");
            Rows = rows;
            Cols = cols;
            CellWidth = cellWidth;
            CellHeight = cellHeight;
            AverageCellDistance = (cellWidth + cellHeight) / 2;
            centers = new Vec2[rows * cols];
            neighbors = new int[rows * cols][];

            // Same layout the old Map.SpawnNodes produced (board centered on the world origin).
            var rootX = cellWidth / 2;
            var rootY = cellHeight / 2 - cellHeight / 8;
            for (var r = 0; r < rows; r++) {
                var rowShift = (r & 1) == 0 ? cellWidth / 4 : -cellWidth / 4;
                for (var c = 0; c < cols; c++) {
                    centers[r * cols + c] = new Vec2(
                        (c - cols / 2) * cellWidth + rootX + rowShift,
                        (r - rows / 2) * cellHeight * 3 / 4 + rootY);
                }
            }

            var buffer = new List<int>(6);
            for (var r = 0; r < rows; r++) {
                for (var c = 0; c < cols; c++) {
                    buffer.Clear();
                    var cell = new HexCoord(r, c);
                    for (var d = 0; d < 6; d++) {
                        var n = cell.Neighbor(d);
                        if (Contains(n)) buffer.Add(IndexOf(n));
                    }
                    neighbors[r * cols + c] = buffer.ToArray();
                }
            }
        }

        public bool Contains(HexCoord cell) => cell.Row >= 0 && cell.Row < Rows && cell.Col >= 0 && cell.Col < Cols;
        public int IndexOf(HexCoord cell) => cell.Row * Cols + cell.Col;
        public HexCoord CoordOf(int index) => new(index / Cols, index % Cols);
        public Vec2 Center(HexCoord cell) => centers[IndexOf(cell)];
        public Vec2 Center(int index) => centers[index];
        public int[] NeighborsOf(int index) => neighbors[index];

        public int NearestIndex(Vec2 position) {
            var best = 0;
            var bestSqr = float.MaxValue;
            for (var i = 0; i < centers.Length; i++) {
                var d = Vec2.SqrDistance(centers[i], position);
                if (d < bestSqr) {
                    bestSqr = d;
                    best = i;
                }
            }
            return best;
        }

        public HexCoord Nearest(Vec2 position) => CoordOf(NearestIndex(position));

        /// <summary>Nearest cell to <paramref name="position"/> accepted by <paramref name="filter"/>, or -1.</summary>
        public int NearestIndex(Vec2 position, Func<int, bool> filter, int exclude = -1) {
            var best = -1;
            var bestSqr = float.MaxValue;
            for (var i = 0; i < centers.Length; i++) {
                if (i == exclude || !filter(i)) continue;
                var d = Vec2.SqrDistance(centers[i], position);
                if (d < bestSqr) {
                    bestSqr = d;
                    best = i;
                }
            }
            return best;
        }

        public int Distance(int a, int b) => HexCoord.Distance(CoordOf(a), CoordOf(b));
    }
}
