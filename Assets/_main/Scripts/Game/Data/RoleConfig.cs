using AutoChess.Core;
using UnityEngine;

namespace AutoChess.Game {
    [CreateAssetMenu(fileName = "Role", menuName = "Destiny/Role")]
    public class RoleConfig : DestinyConfig {
        public Role role;

        public override TraitId TraitId => TraitId.Of(role);

        public override string GetName() {
            return role.ToName();
        }

        public override Sprite GetIcon() {
            return AssetDB.Instance.GetRoleIcon(role)?.value;
        }
    }
}
