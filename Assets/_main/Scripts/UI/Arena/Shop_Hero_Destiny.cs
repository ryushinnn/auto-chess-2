using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Shop_Hero_Destiny : MonoBehaviour {
    [SerializeField] Image image;
    [SerializeField] TMP_Text name;

    public void Initialize(Role role) {
        image.sprite = AssetDB.Instance.GetRoleIcon(role).value;
        name.text = role.ToName();
    }
    
    public void Initialize(Realm realm) {
        image.sprite = AssetDB.Instance.GetRealmIcon(realm).value;
        name.text = realm.ToName();
    }
}