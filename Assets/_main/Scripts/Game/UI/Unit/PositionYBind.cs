using UnityEngine;

namespace AutoChess.Game {
    public class PositionYBind : MonoBehaviour {
        [SerializeField] Transform target;

        void LateUpdate() {
            var p = transform.position;
            transform.position = new Vector3(p.x, target.position.y, p.z);
        }
    }
}
