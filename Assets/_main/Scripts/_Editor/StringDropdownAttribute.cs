using UnityEngine;

public class StringDropdownAttribute : PropertyAttribute {
    public System.Type targetType;

    public StringDropdownAttribute(System.Type targetType) {
        this.targetType = targetType;
    }
}