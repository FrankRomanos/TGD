using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class DotHotModifierDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("DoT / HoT Modifier", EditorStyles.boldLabel);

            var opProp = elem.FindPropertyRelative("dotHotOperation");
            EditorGUILayout.PropertyField(opProp, new GUIContent("Operation"));
            DotHotOperation operation = opProp != null
                ? (DotHotOperation)opProp.enumValueIndex
                : DotHotOperation.TriggerDots;

            if (operation == DotHotOperation.ConvertDamageToDot)
            {
                var maskProp = elem.FindPropertyRelative("visibleFields");
                if (maskProp != null)
                    maskProp.intValue |= (int)EffectFieldMask.Duration;
            }

            var categoryProp = elem.FindPropertyRelative("dotHotCategory");
            EditorGUILayout.PropertyField(categoryProp, new GUIContent("Category"));
            if (categoryProp != null && (DotHotCategory)categoryProp.enumValueIndex == DotHotCategory.Custom)
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("dotHotCustomTag"), new GUIContent("Custom Tag"));

            if (operation != DotHotOperation.ConvertDamageToDot)
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("dotHotTriggerCount"), new GUIContent("Base Trigger Count"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Values"))
            {
                bool collapsed;
                if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
                {
                    if (!collapsed)
                    {
                        PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("valueExprLevels"), "Value Expression by Level");
                        if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                            PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("probabilityLvls"), "Probability by Level (%)");
                    }

                    int curLv = LevelContext.GetSkillLevel(elem.serializedObject);
                    bool showDuration = FieldVisibilityUI.Has(elem, EffectFieldMask.Duration);
                    bool showProbability = FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, curLv, showDuration, showProbability);
                }
                else
                {
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("valueExpression"), new GUIContent("Value Expression"));
                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));
                }
            }
            else
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("valueExpression"), new GUIContent("Value Expression"));
            }

            bool forceDuration = operation == DotHotOperation.ConvertDamageToDot;
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Duration, "Duration") || forceDuration)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("duration"), new GUIContent("Duration (Turns)"));
            }

            if (operation == DotHotOperation.ConvertDamageToDot)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("damageSchool"), new GUIContent("Damage School"));
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("canCrit"), new GUIContent("Can Crit"));
            }

            EditorGUILayout.PropertyField(elem.FindPropertyRelative("dotHotAffectsAllies"), new GUIContent("Affects Allies"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("dotHotAffectsEnemies"), new GUIContent("Affects Enemies"));

            var extra = elem.FindPropertyRelative("dotHotAdditionalEffects");
            if (extra != null)
            {
                // Show nested effect list with proper depth tracking so designers can edit entries safely.
                NestedEffectListDrawer.DrawEffectsList(extra, elem.depth + 1, "Additional Effects");
            }
            else
                EditorGUILayout.HelpBox("'dotHotAdditionalEffects' property not found on effect.", MessageType.Warning);

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                var cond = elem.FindPropertyRelative("condition");
                EditorGUILayout.PropertyField(cond, new GUIContent("Trigger Condition"));
                FieldVisibilityUI.DrawConditionFields(elem, cond);
            }
        }
    }
}