using System;
using AutoChess.Core;
using Sirenix.OdinInspector;

namespace AutoChess.Game {
    /// <summary>Serialized stat modifier of items and evolutions (converted to Core.StatModifier).</summary>
    [Serializable]
    public class AttributeModifier {
        public enum Type {
            FixedValue,
            Percentage
        }

        const string ID_PLACE_HOLDER = "<will be auto generated>";

        [HideIf("@this.id == AttributeModifier.ID_PLACE_HOLDER")]
        public string id = ID_PLACE_HOLDER;
        [StringDropdown(typeof(StatKeys))]
        public string key;
        public float value;
        public Type type;

        public StatModifier ToCore() => new(StatKeys.Parse(key), value,
            type == Type.FixedValue ? ModifierType.Flat : ModifierType.Percent);
    }
}
