using System;

namespace AutoChess.Core {
    /// <summary>
    /// Cell of the battle board in "even-r" offset coordinates: even rows are shifted half a cell to the right.
    /// Row 0 is the home edge. Same convention as the old Map (MapNode.X = row, MapNode.Y = column), so the
    /// grid positions stored in Stage assets keep their meaning.
    /// </summary>
    [Serializable]
    public readonly struct HexCoord : IEquatable<HexCoord> {
        public readonly int Row;
        public readonly int Col;

        public HexCoord(int row, int col) {
            Row = row;
            Col = col;
        }

        // Neighbour offsets (row, col) for even and odd rows, in the order TopLeft, TopRight, Left, Right, BotLeft, BotRight.
        static readonly int[] EvenRowDr = { -1, -1, 0, 0, 1, 1 };
        static readonly int[] EvenRowDc = { 0, 1, -1, 1, 0, 1 };
        static readonly int[] OddRowDr = { -1, -1, 0, 0, 1, 1 };
        static readonly int[] OddRowDc = { -1, 0, -1, 1, -1, 0 };

        public HexCoord Neighbor(int direction) {
            var even = (Row & 1) == 0;
            return even
                ? new HexCoord(Row + EvenRowDr[direction], Col + EvenRowDc[direction])
                : new HexCoord(Row + OddRowDr[direction], Col + OddRowDc[direction]);
        }

        /// <summary>Number of steps between two cells on an unbounded hex grid.</summary>
        public static int Distance(HexCoord a, HexCoord b) {
            // even-r offset -> cube
            var aq = a.Col - ((a.Row + (a.Row & 1)) >> 1);
            var bq = b.Col - ((b.Row + (b.Row & 1)) >> 1);
            var dq = aq - bq;
            var dr = a.Row - b.Row;
            var ds = -dq - dr;
            return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(ds)) / 2;
        }

        /// <summary>180 degree rotation of the board (how a visiting player's home half is shown).</summary>
        public HexCoord Mirror(int rows, int cols) => new(rows - 1 - Row, cols - 1 - Col);

        public bool Equals(HexCoord other) => Row == other.Row && Col == other.Col;
        public override bool Equals(object obj) => obj is HexCoord other && Equals(other);
        public override int GetHashCode() => (Row << 8) ^ Col;
        public static bool operator ==(HexCoord a, HexCoord b) => a.Equals(b);
        public static bool operator !=(HexCoord a, HexCoord b) => !a.Equals(b);
        public override string ToString() => $"{Row},{Col}";

        public static bool TryParse(string text, out HexCoord coord) {
            coord = default;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out var r) || !int.TryParse(parts[1].Trim(), out var c)) return false;
            coord = new HexCoord(r, c);
            return true;
        }
    }
}
