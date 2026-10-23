using RExt.Patterns.Singleton;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>World placement of the bench slots in front of the board (was Deck).</summary>
    public class BenchSpace : Singleton<BenchSpace> {
        [SerializeField] Transform nodeParent;
        [SerializeField] float nodeWidth;
        [SerializeField] float nodeHeight;

        // A drop counts as "on the bench" only inside this area of the world.
        const float LIMIT_X_LEFT = -8.4f;
        const float LIMIT_X_RIGHT = 7.1f;
        const float LIMIT_Z_UP = -6.6f;

        public int Size { get; private set; } = 9;
        public float SlotWidth => nodeWidth;
        public float SlotHeight => nodeHeight;

        public void Bind(int size) {
            Size = size;
        }

        public Vector3 SlotToWorld(int slot) => new Vector3((slot - Size / 2) * nodeWidth, 0, 0) + nodeParent.position;

        /// <summary>Nearest slot to a world point inside the bench area.</summary>
        public bool TryNearestSlot(Vector3 position, out int slot) {
            slot = -1;
            if (position.x < LIMIT_X_LEFT || position.x > LIMIT_X_RIGHT || position.z > LIMIT_Z_UP) return false;
            var best = float.MaxValue;
            for (var i = 0; i < Size; i++) {
                var d = (SlotToWorld(i) - position).sqrMagnitude;
                if (d < best) {
                    best = d;
                    slot = i;
                }
            }
            return slot >= 0;
        }
    }
}
