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

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Target"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new GUIContent("Target"));

            if (operation == DotHotOperation.TriggerDots || operation == DotHotOperation.ConvertDamageToDot)
            {
                if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.School, "Damage School"))
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("damageSchool"), new GUIContent("School"));
            }

            if (operation != DotHotOperation.None)
            {
                if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Crit, "Critical"))
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("canCrit"), new GUIContent("Can Crit"));


                if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Values"))
                {
                    bool collapsed;
                    if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
                    {
                        if (!collapsed)
                            PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("valueExprLevels"), "Tick Value Expression by Level");

                        bool showProbabilityLevels = FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability");
                        if (!collapsed && showProbabilityLevels)
                            PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("probabilityLvls"), "Probability by Level (%)");

                        int curLv = LevelContext.GetSkillLevel(elem.serializedObject);
                        bool showDurationPreview = FieldVisibilityUI.Has(elem, EffectFieldMask.Duration);
                        bool showProbabilityPreview = FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
                        PerLevelUI.DrawPreviewForCurrentLevel(elem, curLv, showDurationPreview, showProbabilityPreview);
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("valueExpression"), new GUIContent("Tick Value Expression"));
                        if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                            EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));
                    }
                }
                else
                {
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("valueExpression"), new GUIContent("Tick Value Expression"));
                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));
                }
            }
            else
            {
                if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));
            }
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("dotHotBaseTriggerCount"), new GUIContent("Base Trigger Count"));
            EditorGUILayout.HelpBox("Base trigger count defines the tick interval. 0 uses the default 6-second round length when calculating DoT/HoT outcomes.", MessageType.Info);

            bool forceDuration = operation == DotHotOperation.ConvertDamageToDot;
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Duration, "Duration") || forceDuration)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("duration"), new GUIContent("Duration (Turns)"));
            }

            var extra = elem.FindPropertyRelative("dotHotAdditionalEffects");
            if (extra != null)
            {
                // Show nested effect list with proper depth tracking so designers can edit entries safely.
                NestedEffectListDrawer.DrawEffectsList(extra, elem.depth + 1, "Additional Effects");
                EditorGUILayout.HelpBox("Additional effects trigger alongside each tick. Configure their durations inside each nested effect entry as needed.", MessageType.Info);
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