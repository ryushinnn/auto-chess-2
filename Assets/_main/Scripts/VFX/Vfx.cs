using UnityEngine;

public class Vfx : MonoBehaviour {
    public string PrefabName { get; private set; }

    public void SetPrefabName(string prefabName) {
        PrefabName = prefabName;
    }
}