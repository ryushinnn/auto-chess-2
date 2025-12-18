using System;
using RExt.Extensions;
using RExt.Patterns.Singleton;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "AssetDB", menuName = "DB/AssetDB")]
public class AssetDB : ScriptableObjectSingleton<AssetDB> {
    [SerializeField, TableList] Icon[] markIcons;
    [SerializeField, TableList] RoleIcon[] roleIcons;
    [SerializeField, TableList] RealmIcon[] realmIcons;
    [SerializeField, TableList] RankIcon[] rankIcons;

    public Icon GetMarkIcon(string key) {
        return markIcons.Find(x=>x.key == key);
    }

    public RoleIcon GetRoleIcon(Role role) {
        return roleIcons.Find(x=>x.role == role);
    }
    
    public RealmIcon GetRealmIcon(Realm realm) {
        return realmIcons.Find(x=>x.realm == realm);
    }
    
    public Sprite GetRankIcon(HeroRank rank) {
        return rankIcons.Find(x=>x.rank == rank).value;
    }
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