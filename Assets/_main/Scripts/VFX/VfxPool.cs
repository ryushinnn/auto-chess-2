using System.Collections.Generic;
using RExt.Extensions;
using RExt.Patterns.Singleton;
using UnityEngine;

public class VfxPool : Singleton<VfxPool> {
    [SerializeField] GameObject[] vfxPrefabs;
    
    Dictionary<string, Queue<Vfx>> pools = new();

    public Vfx GetVfx(string vfxName) {
        return GetVfx<Vfx>(vfxName);
    }

    public T GetVfx<T>(string vfxName) where T : Vfx {
        if (pools.TryGetValue(vfxName, out var pool)) {
            if (pool.TryDequeue(out var vfx)) {
                vfx.gameObject.SetActive(true);
                return (T)vfx;
            }
        }
        
        var prefab = vfxPrefabs.Find(x=>x.name == vfxName);
        if (prefab != null) {
            var vfx = Instantiate(prefab).AddComponent<T>();
            vfx.SetPrefabName(vfxName);
            vfx.gameObject.SetActive(true);
            return vfx;
        }

        return null;
    }

    public void DestroyVfx(Vfx vfx) {
        if (!pools.ContainsKey(vfx.PrefabName)) {
            pools.Add(vfx.PrefabName, new Queue<Vfx>());
        }

        vfx.gameObject.SetActive(false);
        vfx.transform.SetParent(transform);
        pools[vfx.PrefabName].Enqueue(vfx);
    }
}