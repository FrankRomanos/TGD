using System;
using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>
    /// Shared helpers that draw nested effect lists without creating endless recursion in the inspector.
    /// </summary>
    internal static class NestedEffectListDrawer
    {
        private const int MaxNestedDepth = 8;

        public static void DrawEffectsList(SerializedProperty listProp, int depth, string header)
        {
            if (listProp == null)
            {
                EditorGUILayout.HelpBox("Nested effect list property not found.", MessageType.Warning);
                return;
            }

            if (!listProp.isArray)
            {
                EditorGUILayout.HelpBox("Nested effect container is not an array.", MessageType.Warning);
                return;
            }

            if (depth > MaxNestedDepth)
            {
                EditorGUILayout.HelpBox("Nested effect depth limit reached (8).", MessageType.Warning);
                return;
            }

            if (!string.IsNullOrEmpty(header))
                EditorGUILayout.LabelField(header, EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var effectProp = listProp.GetArrayElementAtIndex(i);
                if (effectProp == null)
                    continue;

                EditorGUILayout.BeginVertical("box");
                DrawSingleEffect(effectProp, depth + 1);
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    listProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2f);
            }

            if (GUILayout.Button("Add Effect"))
            {
                int newIndex = listProp.arraySize;
                listProp.InsertArrayElementAtIndex(newIndex);
                var newElem = listProp.GetArrayElementAtIndex(newIndex);
                ResetEffectProperty(newElem);
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawSingleEffect(SerializedProperty effectProp, int depth)
        {
            var typeProp = effectProp.FindPropertyRelative("effectType");
            EditorGUILayout.PropertyField(typeProp, new GUIContent("Effect Type"));

            var drawer = EffectDrawerRegistry.Get((EffectType)typeProp.enumValueIndex);
            using (new EditorGUI.IndentLevelScope())
            {
                drawer.Draw(effectProp);
            }
        }

        private static void ResetEffectProperty(SerializedProperty effectProp)
        {
            if (effectProp == null)
                return;

            SetEnumValue(effectProp.FindPropertyRelative("effectType"), EffectType.None);
            SetEnumValue(effectProp.FindPropertyRelative("condition"), EffectCondition.None);
            SetEnumValue(effectProp.FindPropertyRelative("target"), TargetType.Self);
            SetEnumValue(effectProp.FindPropertyRelative("attributeType"), AttributeType.Attack);
            SetEnumValue(effectProp.FindPropertyRelative("damageSchool"), DamageSchool.Physical);

            SetString(effectProp.FindPropertyRelative("valueExpression"), string.Empty);
            SetString(effectProp.FindPropertyRelative("probability"), string.Empty);
            SetString(effectProp.FindPropertyRelative("statusSkillID"), string.Empty);
            SetString(effectProp.FindPropertyRelative("targetSkillID"), string.Empty);
            SetString(effectProp.FindPropertyRelative("repeatCountExpression"), string.Empty);
            SetString(effectProp.FindPropertyRelative("dotHotCustomTag"), string.Empty);

            SetInt(effectProp.FindPropertyRelative("stackCount"), 1);
            SetFloat(effectProp.FindPropertyRelative("value"), 0f);
            SetFloat(effectProp.FindPropertyRelative("duration"), 0f);
            SetInt(effectProp.FindPropertyRelative("repeatCount"), 1);
            SetInt(effectProp.FindPropertyRelative("repeatMaxCount"), 0);
            SetInt(effectProp.FindPropertyRelative("dotHotTriggerCount"), 1);

            SetBool(effectProp.FindPropertyRelative("perLevel"), false);
            SetBool(effectProp.FindPropertyRelative("perLevelDuration"), true);
            SetBool(effectProp.FindPropertyRelative("dotHotAffectsAllies"), false);
            SetBool(effectProp.FindPropertyRelative("dotHotAffectsEnemies"), true);
            SetBool(effectProp.FindPropertyRelative("repeatConsumeResource"), true);

            ClearArray(effectProp.FindPropertyRelative("onSuccess"));
            ClearArray(effectProp.FindPropertyRelative("repeatEffects"));
            ClearArray(effectProp.FindPropertyRelative("dotHotAdditionalEffects"));
            ClearArray(effectProp.FindPropertyRelative("randomOutcomes"));
        }

        private static void SetEnumValue(SerializedProperty prop, Enum value)
        {
            if (prop == null)
                return;
            prop.enumValueIndex = Convert.ToInt32(value);
        }

        private static void SetString(SerializedProperty prop, string value)
        {
            if (prop == null)
                return;
            prop.stringValue = value ?? string.Empty;
        }

        private static void SetInt(SerializedProperty prop, int value)
        {
            if (prop == null)
                return;
            prop.intValue = value;
        }

        private static void SetFloat(SerializedProperty prop, float value)
        {
            if (prop == null)
                return;
            prop.floatValue = value;
        }

        private static void SetBool(SerializedProperty prop, bool value)
        {
            if (prop == null)
                return;
            prop.boolValue = value;
        }

        private static void ClearArray(SerializedProperty prop)
        {
            if (prop == null || !prop.isArray)
                return;

            while (prop.arraySize > 0)
                prop.DeleteArrayElementAtIndex(prop.arraySize - 1);
        }
    }
}