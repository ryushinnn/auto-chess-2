using UnityEngine;

namespace AutoChess.Game {
    public class PositionBind : MonoBehaviour {
        [SerializeField] float offsetY;
        Transform target;

        public void SetTarget(Transform followed, float yOffset = 0) {
            target = followed;
            offsetY = yOffset;
        }

        void Update() {
            if (target) {
                transform.position = new Vector3(0, offsetY, 0) + target.position;
            }
        }
    }
}
