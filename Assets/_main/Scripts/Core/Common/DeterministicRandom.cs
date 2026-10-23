using System;

namespace AutoChess.Core {
    /// <summary>
    /// xoshiro128** generator. Identical sequences on every platform, cheap to copy and to serialize, so a
    /// match (or a single battle) replays exactly from its seed - the property a server-authoritative or
    /// lockstep multiplayer build and the regression tests rely on. Never use System.Random or
    /// UnityEngine.Random inside the simulation.
    /// </summary>
    public sealed class DeterministicRandom {
        uint s0, s1, s2, s3;

        public DeterministicRandom(ulong seed) {
            Reseed(seed);
        }

        DeterministicRandom(uint a, uint b, uint c, uint d) {
            s0 = a;
            s1 = b;
            s2 = c;
            s3 = d;
        }

        public void Reseed(ulong seed) {
            var sm = seed;
            s0 = (uint)SplitMix(ref sm);
            s1 = (uint)SplitMix(ref sm);
            s2 = (uint)SplitMix(ref sm);
            s3 = (uint)SplitMix(ref sm);
            if ((s0 | s1 | s2 | s3) == 0) s0 = 1;
        }

        public uint NextUInt() {
            var result = RotL(s1 * 5, 7) * 9;
            var t = s1 << 9;
            s2 ^= s0;
            s3 ^= s1;
            s1 ^= s2;
            s0 ^= s3;
            s2 ^= t;
            s3 = RotL(s3, 11);
            return result;
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextFloat() => (NextUInt() >> 8) * (1d / 16777216d);

        /// <summary>Uniform int in [minInclusive, maxExclusive). Returns min when the range is empty.</summary>
        public int Range(int minInclusive, int maxExclusive) {
            if (maxExclusive <= minInclusive) return minInclusive;
            var span = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)((ulong)NextUInt() * span >> 32);
        }

        public bool Chance(double probability) => NextFloat() < probability;

        /// <summary>A new independent stream derived from this one (advances this generator once).</summary>
        public DeterministicRandom Fork(uint salt) {
            ulong seed = ((ulong)NextUInt() << 32) | (NextUInt() ^ salt);
            return new DeterministicRandom(seed);
        }

        public RandomState GetState() => new(s0, s1, s2, s3);

        public void SetState(RandomState state) {
            s0 = state.S0;
            s1 = state.S1;
            s2 = state.S2;
            s3 = state.S3;
        }

        public DeterministicRandom Clone() => new(s0, s1, s2, s3);

        static uint RotL(uint x, int k) => (x << k) | (x >> (32 - k));

        static ulong SplitMix(ref ulong x) {
            var z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    [Serializable]
    public readonly struct RandomState : IEquatable<RandomState> {
        public readonly uint S0, S1, S2, S3;

        public RandomState(uint s0, uint s1, uint s2, uint s3) {
            S0 = s0;
            S1 = s1;
            S2 = s2;
            S3 = s3;
        }

        public bool Equals(RandomState other) => S0 == other.S0 && S1 == other.S1 && S2 == other.S2 && S3 == other.S3;
        public override bool Equals(object obj) => obj is RandomState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(S0, S1, S2, S3);
    }
}
