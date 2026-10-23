using System;
using AutoChess.Core;
using RExt.Patterns.Singleton;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>
    /// World placement of the hex battle board (was Map). The simulation plane maps 1:1 onto the world XZ
    /// plane (board centered on the origin); this component only supplies the cell size from the scene.
    /// </summary>
    public class BoardSpace : Singleton<BoardSpace> {
        [SerializeField] Transform nodeParent;
        [SerializeField] float nodeWidth;
        [SerializeField] float nodeHeight;

        HexGrid grid;

        public float CellWidth => nodeWidth;
        public float CellHeight => nodeHeight;
        public HexGrid Grid => grid;
        public Transform NodeParent => nodeParent;

        public void Bind(HexGrid hexGrid) {
            grid = hexGrid;
        }

        public static Vector3 ToWorld(Vec2 p) => new(p.X, 0, p.Y);
        public static Vec2 ToPlane(Vector3 p) => new(p.x, p.z);

        public Vector3 CellToWorld(HexCoord cell) => ToWorld(grid.Center(cell));
        public Vector3 CellToWorld(int index) => ToWorld(grid.Center(index));

        public HexCoord NearestCell(Vector3 position) => grid.Nearest(ToPlane(position));

        /// <summary>Nearest cell accepted by <paramref name="filter"/>, or false when none is.</summary>
        public bool TryNearestCell(Vector3 position, Func<HexCoord, bool> filter, out HexCoord cell) {
            var index = grid.NearestIndex(ToPlane(position), i => filter(grid.CoordOf(i)));
            cell = index >= 0 ? grid.CoordOf(index) : default;
            return index >= 0;
        }
    }
}
