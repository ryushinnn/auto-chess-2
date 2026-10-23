using UnityEngine;

namespace AutoChess.Game {
    public class Vfx : MonoBehaviour {
        public string PrefabName { get; private set; }

        public void SetPrefabName(string prefabName) {
            PrefabName = prefabName;
        }
    }
}
