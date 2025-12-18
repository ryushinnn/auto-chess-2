using UnityEngine;

[CreateAssetMenu(fileName = "Realm", menuName = "Destiny/Realm")]
public class RealmConfig : DestinyConfig {
    public Realm realm;

    public override string GetName() {
        return realm.ToName();
    }

    public override Sprite GetIcon() {
        return AssetDB.Instance.GetRealmIcon(realm).value;
    }
}