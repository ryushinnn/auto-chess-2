using AutoChess.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace AutoChess.Game {
    public class Shop_Hero_Destiny : MonoBehaviour {
        [SerializeField] Image image;
        [SerializeField, FormerlySerializedAs("name")] TMP_Text nameText;

        public void Initialize(Role role) {
            image.sprite = AssetDB.Instance.GetRoleIcon(role)?.value;
            nameText.text = role.ToName();
        }

        public void Initialize(Realm realm) {
            image.sprite = AssetDB.Instance.GetRealmIcon(realm)?.value;
            nameText.text = realm.ToName();
        }
    }
}
