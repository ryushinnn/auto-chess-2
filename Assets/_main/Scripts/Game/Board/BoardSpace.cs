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

        public static Vector3 ToWorld(Vec2 p) => new((float)p.X, 0, (float)p.Y);

        /// <summary>
        /// The visible battle is drawn rotated half a turn: the viewed player is its visiting (Away) team, and like
        /// in TFT a player always sees their own units on the near half. The grid is point-symmetric around the
        /// origin (HexCoord.Mirror), so rotating the plane maps every cell onto its mirror.
        /// </summary>
        public static bool BattleFlipped;

        /// <summary>World position of a battle-plane point, rotated when <see cref="BattleFlipped"/>.</summary>
        public static Vector3 BattleToWorld(Vec2 p) => BattleFlipped ? new Vector3(-(float)p.X, 0, -(float)p.Y) : ToWorld(p);
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
