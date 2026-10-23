using Sirenix.OdinInspector;
using UnityEngine;

namespace AutoChess.Game {
    [CreateAssetMenu(menuName = "Item")]
    public class Item : ScriptableObject {
        public new string name;
        [PreviewField] public Sprite icon;
        public Item[] ingredients;
        public AttributeModifier[] modifiers;

        /// <summary>Stable id used by the simulation: the asset name (e.g. "_armor", "armor+crit").</summary>
        public string Id => ((Object)this).name;

        public bool IsForgedItem() {
            return ingredients != null && ingredients.Length == 2;
        }
    }
}
