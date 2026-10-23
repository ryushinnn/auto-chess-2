using System.Collections.Generic;
using RExt.Patterns.Singleton;
using UnityEngine;

namespace AutoChess.Game {
    public class VfxPool : Singleton<VfxPool> {
        [SerializeField] GameObject[] vfxPrefabs;

        readonly Dictionary<string, Queue<Vfx>> pools = new();
        Dictionary<string, GameObject> prefabsByName;

        public Vfx GetVfx(string vfxName) {
            return GetVfx<Vfx>(vfxName);
        }

        public T GetVfx<T>(string vfxName) where T : Vfx {
            if (string.IsNullOrEmpty(vfxName)) return null;
            if (pools.TryGetValue(vfxName, out var pool)) {
                while (pool.TryDequeue(out var pooled)) {
                    if (pooled is T typed) {
                        pooled.gameObject.SetActive(true);
                        return typed;
                    }
                    Destroy(pooled.gameObject);
                }
            }

            prefabsByName ??= BuildIndex();
            if (!prefabsByName.TryGetValue(vfxName, out var prefab)) {
                Debug.LogWarning($"VfxPool: no VFX prefab named '{vfxName}'");
                return null;
            }
            var instance = Instantiate(prefab);
            if (!instance.TryGetComponent(out T vfx)) vfx = instance.AddComponent<T>();
            vfx.SetPrefabName(vfxName);
            vfx.gameObject.SetActive(true);
            return vfx;
        }

        Dictionary<string, GameObject> BuildIndex() {
            var index = new Dictionary<string, GameObject>();
            foreach (var prefab in vfxPrefabs) {
                if (prefab != null) index[prefab.name] = prefab;
            }
            return index;
        }

        public void DestroyVfx(Vfx vfx) {
            if (vfx == null) return;
            if (!pools.TryGetValue(vfx.PrefabName, out var pool)) {
                pool = new Queue<Vfx>();
                pools.Add(vfx.PrefabName, pool);
            }
            vfx.gameObject.SetActive(false);
            vfx.transform.SetParent(transform);
            pool.Enqueue(vfx);
        }
    }
}
