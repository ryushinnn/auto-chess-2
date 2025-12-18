using UnityEngine;

[CreateAssetMenu(fileName = "Role", menuName = "Destiny/Role")]
public class RoleConfig : DestinyConfig {
    public Role role;

    public override string GetName() {
        return role.ToName();
    }

    public override Sprite GetIcon() {
        return AssetDB.Instance.GetRoleIcon(role).value;
    }
}