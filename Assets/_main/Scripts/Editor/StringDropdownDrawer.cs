using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AutoChess.Game.Editor {
    [CustomPropertyDrawer(typeof(StringDropdownAttribute))]
    public class StringDropdownDrawer : PropertyDrawer {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            var dropdown = (StringDropdownAttribute)attribute;
            var options = new List<string>();
            foreach (var field in dropdown.targetType.GetFields(BindingFlags.Public | BindingFlags.Static)) {
                if (field.FieldType == typeof(string)) options.Add((string)field.GetValue(null));
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
}
