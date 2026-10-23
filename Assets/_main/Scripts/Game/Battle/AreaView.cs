using UnityEngine;

namespace AutoChess.Game {
    /// <summary>Ground marker of a damage area (was DamageOverTimeArea; the strikes are simulated in Core).</summary>
    public class AreaView : MonoBehaviour {
        [SerializeField] LayerMask targetLayerMask;
        [SerializeField] Transform graphic;

        public void Show(float radius) {
            graphic.localScale = new Vector3(radius * 2, radius * 2, 1);
        }
    }
}
