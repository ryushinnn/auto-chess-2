using System;

namespace AutoChess.Core {
    /// <summary>
    /// Position on the battle plane. X maps to world X and Y to world Z (the board lies on the XZ plane).
    /// </summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2> {
        public float X;
        public float Y;

        public Vec2(float x, float y) {
            X = x;
            Y = y;
        }

        public static readonly Vec2 Zero = new(0, 0);

        public float Length => MathF.Sqrt(X * X + Y * Y);
        public float SqrLength => X * X + Y * Y;

        public Vec2 Normalized {
            get {
                var len = Length;
                return len > 1e-6f ? new Vec2(X / len, Y / len) : Zero;
            }
        }

        public static float Distance(Vec2 a, Vec2 b) {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        public static float SqrDistance(Vec2 a, Vec2 b) {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        /// <summary>Moves <paramref name="current"/> towards <paramref name="target"/> by at most <paramref name="maxDelta"/>.</summary>
        public static Vec2 MoveTowards(Vec2 current, Vec2 target, float maxDelta, out bool arrived) {
            var dx = target.X - current.X;
            var dy = target.Y - current.Y;
            var sqr = dx * dx + dy * dy;
            if (sqr <= maxDelta * maxDelta || sqr < 1e-12f) {
                arrived = true;
                return target;
            }
            arrived = false;
            var len = MathF.Sqrt(sqr);
            return new Vec2(current.X + dx / len * maxDelta, current.Y + dy / len * maxDelta);
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float k) => new(a.X * k, a.Y * k);
        public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
        public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }
}
