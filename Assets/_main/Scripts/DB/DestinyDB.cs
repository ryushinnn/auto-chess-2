using System.Collections.Generic;
using RExt.Extensions;
using RExt.Patterns.Singleton;
using UnityEngine;

[CreateAssetMenu(fileName = "DestinyDB", menuName = "DB/DestinyDB")]
public class DestinyDB : ScriptableObjectSingleton<DestinyDB> {
    [SerializeField] RoleConfig[] roleConfigs;
    [SerializeField] RealmConfig[] realmConfigs;

    readonly Dictionary<Role, DestinyConfig> cachedRoleDestinies = new();
    readonly Dictionary<Realm, DestinyConfig> cachedRealmDestinies = new();

    public DestinyConfig Find(Role role) {
        if (!cachedRoleDestinies.ContainsKey(role)) {
            cachedRoleDestinies[role] = roleConfigs.Find(x => x.role == role);
        }
        return cachedRoleDestinies[role];
    }

    public DestinyConfig Find(Realm realm) {
        if (!cachedRealmDestinies.ContainsKey(realm)) {
            cachedRealmDestinies[realm] = realmConfigs.Find(x => x.realm == realm);
        }
        return cachedRealmDestinies[realm];
    }
}