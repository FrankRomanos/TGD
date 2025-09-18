using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class ApplyStatusDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Apply Status (Buff/Debuff)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("statusSkillID"), new GUIContent("Status Skill ID"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Values"))
            {
                bool collapsed;
                if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
                {
                    if (!collapsed)
                    {
                        if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Duration, "Duration"))
                        {
                            PerLevelUI.DrawIntLevels(elem.FindPropertyRelative("durationLevels"), "Duration by Level (turns)");
                            DrawDurationHelpBox();
                        }


                        if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                            PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("probabilityLvls"), "Probability by Level (%)");
                        if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Stacks, "Stacks"))
                            PerLevelUI.DrawIntLevels(elem.FindPropertyRelative("stackCountLevels"), "Stacks by Level");
                    }

                    int curLv = LevelContext.GetSkillLevel(elem.serializedObject);
                    bool showD = FieldVisibilityUI.Has(elem, EffectFieldMask.Duration);
                    bool showP = FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
                    bool showStacks = FieldVisibilityUI.Has(elem, EffectFieldMask.Stacks);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, curLv, showD, showP, showStacks, showValue: false);
                }
                else
                {
                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Duration, "Duration"))
                        DrawDurationField(elem);

                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));

                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Stacks, "Stacks"))
                        DrawStackCountField(elem);
                }
            }

            if (FieldVisibilityUI.Has(elem, EffectFieldMask.Stacks))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("maxStacks"), new GUIContent("Max Stacks (0 = Unlimited)"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Target"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new GUIContent("Target"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                var conditionProp = elem.FindPropertyRelative("condition");
                EditorGUILayout.PropertyField(conditionProp, new GUIContent("Trigger Condition"));
                FieldVisibilityUI.DrawConditionFields(elem, conditionProp);
            }
        }
        private void DrawStackCountField(SerializedProperty elem)
        {
            var stacksProp = elem.FindPropertyRelative("stackCount");
            if (stacksProp == null)
            {
                EditorGUILayout.HelpBox("'stackCount' property not found on effect.", MessageType.Warning);
                return;
            }

            EditorGUILayout.PropertyField(stacksProp, new GUIContent("Stacks to Apply"));
            if (stacksProp.intValue < 1)
                stacksProp.intValue = 1;
        }

        private void DrawDurationField(SerializedProperty elem)
        {
            var durationProp = elem.FindPropertyRelative("duration");
            if (durationProp == null)
            {
                EditorGUILayout.HelpBox("'duration' property not found on effect.", MessageType.Warning);
                return;
            }

            int currentValue = durationProp.propertyType == SerializedPropertyType.Integer
                ? durationProp.intValue
                : Mathf.RoundToInt(durationProp.floatValue);

            int newValue = EditorGUILayout.IntField(new GUIContent("Duration (turns)"), currentValue);
            if (durationProp.propertyType == SerializedPropertyType.Integer)
                durationProp.intValue = newValue;
            else
                durationProp.floatValue = newValue;

            DrawDurationHelpBox();
        }

        private void DrawDurationHelpBox()
        {
            EditorGUILayout.HelpBox("0 or empty = no duration. -1 = instant trigger. -2 = permanent.", MessageType.Info);
        }
    }
}

