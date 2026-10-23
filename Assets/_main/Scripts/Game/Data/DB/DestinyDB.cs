using System.Collections.Generic;
using AutoChess.Core;
using RExt.Patterns.Singleton;
using UnityEngine;

namespace AutoChess.Game {
    [CreateAssetMenu(fileName = "DestinyDB", menuName = "DB/DestinyDB")]
    public class DestinyDB : ScriptableObjectSingleton<DestinyDB> {
        [SerializeField] RoleConfig[] roleConfigs;
        [SerializeField] RealmConfig[] realmConfigs;

        Dictionary<TraitId, DestinyConfig> byId;

        public IEnumerable<DestinyConfig> All {
            get {
                foreach (var r in roleConfigs) yield return r;
                foreach (var r in realmConfigs) yield return r;
            }
        }

        public DestinyConfig Find(TraitId id) {
            if (byId == null) {
                byId = new Dictionary<TraitId, DestinyConfig>();
                foreach (var c in All) {
                    if (c != null) byId[c.TraitId] = c;
                }
            }
            return byId.TryGetValue(id, out var config) ? config : null;
        }

        public DestinyConfig Find(Role role) => Find(TraitId.Of(role));
        public DestinyConfig Find(Realm realm) => Find(TraitId.Of(realm));

#if UNITY_EDITOR
        void OnValidate() => byId = null;
#endif
    }
}
