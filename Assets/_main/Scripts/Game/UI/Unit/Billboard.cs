using UnityEngine;

namespace AutoChess.Game {
    public class Billboard : MonoBehaviour {
        Transform cam;

        void Awake() {
            var main = Camera.main;
            if (main != null) cam = main.transform;
        }

        void LateUpdate() {
            if (cam == null) return;
            var rotation = cam.rotation;
            transform.LookAt(transform.position + rotation * Vector3.forward, rotation * Vector3.up);
        }
    }
}
