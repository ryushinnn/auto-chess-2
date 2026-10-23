using UnityEngine;

namespace AutoChess.Game {
    public class ScalableVfx : Vfx {
        float originalScale = -1;

        void Awake() {
            if (originalScale < 0) originalScale = transform.localScale.x;
        }

        public void SetScale(Vector3 scale) {
            if (originalScale < 0) originalScale = transform.localScale.x;
            transform.localScale = originalScale * scale;
        }
    }
}
