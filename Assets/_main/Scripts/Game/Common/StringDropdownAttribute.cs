using UnityEngine;

namespace AutoChess.Game {
    /// <summary>Shows a string field as a dropdown of the public string constants of <see cref="targetType"/>.</summary>
    public class StringDropdownAttribute : PropertyAttribute {
        public System.Type targetType;

        public StringDropdownAttribute(System.Type targetType) {
            this.targetType = targetType;
        }
    }
}
