using Sirenix.OdinInspector;
using UnityEngine;

namespace AutoChess.Game {
    public abstract class Cell : MonoBehaviour {
        [SerializeField] MeshRenderer meshRenderer;
        [SerializeField] Material normalMat;
        [SerializeField] Material occupiedMat;
        [SerializeField] Material highlightMat;
        [SerializeField] Material notAvailableMat;

        [SerializeField, ReadOnly] bool occupied;
        [SerializeField, ReadOnly] bool highlight;
        [SerializeField, ReadOnly] bool notAvailable;

        public bool Occupied => occupied;

        // blue mark. lowest priority
        public void SetOccupied(bool value) {
            if (occupied == value) return;
            occupied = value;
            UpdateMaterial();
        }

        // green mark. medium priority
        public void SetHighlight(bool value) {
            if (highlight == value) return;
            highlight = value;
            UpdateMaterial();
        }

        // red mark. highest priority
        public void SetNotAvailable(bool value) {
            if (notAvailable == value) return;
            notAvailable = value;
            UpdateMaterial();
        }

        void UpdateMaterial() {
            meshRenderer.sharedMaterial = notAvailable ? notAvailableMat
                : highlight ? highlightMat
                : occupied ? occupiedMat
                : normalMat;
        }
    }
}
