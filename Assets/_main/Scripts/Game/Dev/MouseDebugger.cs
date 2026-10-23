using UnityEngine;

namespace AutoChess.Game {
    /// <summary>Development helper: logs the collider under every left click when enabled.</summary>
    public class MouseDebugger : MonoBehaviour {
        [SerializeField] bool logClicks;

        void Update() {
            if (!logClicks || !Input.GetMouseButtonDown(0)) return;
            var cam = Camera.main;
            if (cam != null && Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit)) {
                Debug.Log("Hit object: " + hit.collider.gameObject.name);
            }
        }
    }
}
