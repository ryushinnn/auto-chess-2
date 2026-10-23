using System;
using System.Collections.Generic;
using AutoChess.Core;
using RExt.Patterns.Singleton;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AutoChess.Game {
    [CreateAssetMenu(fileName = "AssetDB", menuName = "DB/AssetDB")]
    public class AssetDB : ScriptableObjectSingleton<AssetDB> {
        [SerializeField, TableList] Icon[] markIcons;
        [SerializeField, TableList] RoleIcon[] roleIcons;
        [SerializeField, TableList] RealmIcon[] realmIcons;
        [SerializeField, TableList] RankIcon[] rankIcons;

        Dictionary<string, Icon> marksByKey;

        public Icon GetMarkIcon(string key) {
            if (marksByKey == null) {
                marksByKey = new Dictionary<string, Icon>();
                foreach (var i in markIcons) marksByKey[i.key] = i;
            }
            return key != null && marksByKey.TryGetValue(key, out var icon) ? icon : null;
        }

        public RoleIcon GetRoleIcon(Role role) => Array.Find(roleIcons, x => x.role == role);

        public RealmIcon GetRealmIcon(Realm realm) => Array.Find(realmIcons, x => x.realm == realm);

        public Sprite GetRankIcon(HeroRank rank) => Array.Find(rankIcons, x => x.rank == rank)?.value;

#if UNITY_EDITOR
        void OnValidate() => marksByKey = null;
#endif
    }

    [Serializable]
    public class Icon {
        public string key;
        [PreviewField]
        public Sprite value;
    }

    [Serializable]
    public class RoleIcon {
        public Role role;
        [PreviewField]
        public Sprite value;
    }

    [Serializable]
    public class RealmIcon {
        public Realm realm;
        [PreviewField]
        public Sprite value;
    }

    [Serializable]
    public class RankIcon {
        public HeroRank rank;
        [PreviewField]
        public Sprite value;
    }
}
