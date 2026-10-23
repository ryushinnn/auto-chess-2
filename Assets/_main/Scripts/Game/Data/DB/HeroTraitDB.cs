using System.Collections.Generic;
using RExt.Patterns.Singleton;
using UnityEngine;

namespace AutoChess.Game {
    [CreateAssetMenu(fileName = "HeroTraitDB", menuName = "DB/HeroTraitDB")]
    public class HeroTraitDB : ScriptableObjectSingleton<HeroTraitDB> {
        [SerializeField] HeroTrait[] heroTraits;

        Dictionary<string, HeroTrait> byId;

        public IReadOnlyList<HeroTrait> All => heroTraits;

        public HeroTrait Find(string id) {
            if (byId == null) {
                byId = new Dictionary<string, HeroTrait>();
                foreach (var t in heroTraits) {
                    if (t != null && !string.IsNullOrEmpty(t.id)) byId[t.id] = t;
                }
            }
            return id != null && byId.TryGetValue(id, out var trait) ? trait : null;
        }

#if UNITY_EDITOR
        void OnValidate() => byId = null;
#endif
    }
}
