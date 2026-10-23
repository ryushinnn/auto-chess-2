using System.Collections.Generic;
using RExt.Patterns.Singleton;
using UnityEngine;

namespace AutoChess.Game {
    [CreateAssetMenu(fileName = "ItemDB", menuName = "DB/ItemDB")]
    public class ItemDB : ScriptableObjectSingleton<ItemDB> {
        [SerializeField] Item[] rawItems;
        [SerializeField] Item[] forgedItems;

        Dictionary<string, Item> byId;

        public IReadOnlyList<Item> RawItems => rawItems;
        public IReadOnlyList<Item> ForgedItems => forgedItems;

        public Item Find(string id) {
            if (byId == null) {
                byId = new Dictionary<string, Item>();
                foreach (var i in rawItems) {
                    if (i != null) byId[i.Id] = i;
                }
                foreach (var i in forgedItems) {
                    if (i != null) byId[i.Id] = i;
                }
            }
            return id != null && byId.TryGetValue(id, out var item) ? item : null;
        }

#if UNITY_EDITOR
        void OnValidate() => byId = null;
#endif
    }
}
