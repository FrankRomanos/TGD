#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TGD.CoreV2.Resource;
using UnityEditor;
using UnityEngine;

namespace TGD.CoreV2.Editor
{
    [CustomPropertyDrawer(typeof(UnitResourceHub.SlotSpec))]
    public sealed class UnitResourceHubSlotDrawer : PropertyDrawer
    {
        static readonly string[] s_knownResources =
        {
            "Discipline",
            "Iron",
            "Rage",
            "Versatility",
            "Gunpowder",
            "Point",
            "Combo",
            "Punch",
            "Qi",
            "Vision",
            "Posture",
            "Custom"
        };

        static readonly GUIContent s_resourceLabel = new GUIContent("Resource");
        static readonly GUIContent s_capLabel = new GUIContent("Cap");
        static readonly GUIContent s_startLabel = new GUIContent("Start Value");
        static readonly GUIContent s_clearLabel = new GUIContent("Clear On Turn End");
        static readonly GUIContent s_showLabel = new GUIContent("Show On HUD");

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property == null)
                return;

            var resourceProp = property.FindPropertyRelative("resourceId");
            var capProp = property.FindPropertyRelative("cap");
            var startProp = property.FindPropertyRelative("startValue");
            var clearProp = property.FindPropertyRelative("clearOnTurnEnd");
            var showProp = property.FindPropertyRelative("showOnHud");

            EditorGUI.BeginProperty(position, label, property);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            var line = new Rect(position.x, position.y, position.width, lineHeight);

            DrawResourcePopup(line, resourceProp);
            line.y += (lineHeight * 2f) + spacing * 2f; // popup + manual field

            if (capProp != null)
            {
                EditorGUI.PropertyField(line, capProp, s_capLabel);
                line.y += lineHeight + spacing;
            }

            if (startProp != null)
            {
                EditorGUI.PropertyField(line, startProp, s_startLabel);
                line.y += lineHeight + spacing;
            }

            if (clearProp != null || showProp != null)
            {
                var toggleRect = new Rect(line.x, line.y, line.width, lineHeight);
                float halfWidth = toggleRect.width * 0.5f;

                if (clearProp != null)
                {
                    var clearRect = new Rect(toggleRect.x, toggleRect.y, halfWidth - 2f, lineHeight);
                    bool newValue = EditorGUI.ToggleLeft(clearRect, s_clearLabel, clearProp.boolValue);
                    clearProp.boolValue = newValue;
                }

                if (showProp != null)
                {
                    var showRect = new Rect(toggleRect.x + halfWidth + 2f, toggleRect.y, halfWidth - 2f, lineHeight);
                    bool newValue = EditorGUI.ToggleLeft(showRect, s_showLabel, showProp.boolValue);
                    showProp.boolValue = newValue;
                }
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            const int totalLines = 5; // popup, manual, cap, start, toggles
            return lineHeight * totalLines + spacing * (totalLines - 1);
        }

        static void DrawResourcePopup(Rect rect, SerializedProperty resourceProp)
        {
            EditorGUI.BeginChangeCheck();
            string currentId = resourceProp != null ? resourceProp.stringValue : string.Empty;
            var (options, index) = BuildOptions(currentId);
            int newIndex = EditorGUI.Popup(rect, s_resourceLabel, index, options);
            if (EditorGUI.EndChangeCheck() && resourceProp != null)
            {
                if (newIndex <= 0)
                {
                    resourceProp.stringValue = string.Empty;
                }
                else if (newIndex - 1 < s_knownResources.Length)
                {
                    resourceProp.stringValue = s_knownResources[newIndex - 1];
                }
                else
                {
                    resourceProp.stringValue = currentId;
                }
            }

            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.indentLevel++;
            string manual = resourceProp != null ? resourceProp.stringValue : string.Empty;
            string updated = EditorGUI.DelayedTextField(rect, GUIContent.none, manual);
            EditorGUI.indentLevel--;
            if (resourceProp != null && !string.Equals(updated, manual, StringComparison.Ordinal))
                resourceProp.stringValue = updated;
        }

        static (GUIContent[] options, int index) BuildOptions(string currentId)
        {
            var contents = new List<GUIContent>(s_knownResources.Length + 1)
            {
                new GUIContent("(None)")
            };

            int selectedIndex = 0;
            for (int i = 0; i < s_knownResources.Length; i++)
            {
                string option = s_knownResources[i];
                contents.Add(new GUIContent(option));
                if (!string.IsNullOrEmpty(currentId) && string.Equals(option, currentId, StringComparison.OrdinalIgnoreCase))
                    selectedIndex = i + 1;
            }

            return (contents.ToArray(), selectedIndex);
        }
    }
}
#endif
