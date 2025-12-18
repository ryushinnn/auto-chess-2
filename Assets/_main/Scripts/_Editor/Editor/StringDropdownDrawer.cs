using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(StringDropdownAttribute))]
public class StringDropdownDrawer : PropertyDrawer {
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        var dropdown = (StringDropdownAttribute)attribute;
        var targetType = dropdown.targetType;
        var options = new List<string>();
        foreach (var field in targetType.GetFields(BindingFlags.Public | BindingFlags.Static)) {
            if (field.FieldType == typeof(string)) {
                options.Add((string)field.GetValue(null));
            }
        }

        if (options.Count > 0) {
            var index = Mathf.Max(0, options.IndexOf(property.stringValue));
            index = EditorGUI.Popup(position, label.text, index, options.ToArray());
            property.stringValue = options[index];
        }
        else {
            EditorGUI.PropertyField(position, property, label);
        }
    }
}