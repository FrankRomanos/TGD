using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>
    /// Inspector drawer for EffectType.ModifyStatus. Allows configuring apply, replace and delete operations.
    /// </summary>
    public class ModifyStatusDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Modify Status", EditorStyles.boldLabel);

            var modifyTypeProp = elem.FindPropertyRelative("statusModifyType");
            EditorGUILayout.PropertyField(modifyTypeProp, new GUIContent("Modify Type"));
            var modifyType = (StatusModifyType)modifyTypeProp.enumValueIndex;
            if (modifyType == StatusModifyType.ApplyStatus)
            {
                DrawApplyStatus(elem);
                return;
            }

            DrawModifyControls(elem, modifyType);
        }

        private void DrawApplyStatus(SerializedProperty elem)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Apply Status (Buff/Debuff)", EditorStyles.boldLabel);

            DrawSkillSelectors(elem, StatusModifyType.ApplyStatus);

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
                    bool showDuration = FieldVisibilityUI.Has(elem, EffectFieldMask.Duration);
                    bool showProbability = FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
                    bool showStacks = FieldVisibilityUI.Has(elem, EffectFieldMask.Stacks);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, curLv, showDuration, showProbability, showStacks, showValue: false);
                }
                else
                {
                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Duration, "Duration"))
                        DrawDurationField(elem);

                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));

                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Stacks, "Stacks"))
                        DrawApplyStackField(elem);
                }
            }

            if (FieldVisibilityUI.Has(elem, EffectFieldMask.Stacks))
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("maxStacks"), new GUIContent("Max Stacks (0 = Unlimited)"));
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Target"))
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new GUIContent("Target"));
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                var conditionProp = elem.FindPropertyRelative("condition");
                EditorGUILayout.PropertyField(conditionProp, new GUIContent("Trigger Condition"));
                FieldVisibilityUI.DrawConditionFields(elem, conditionProp);
            }
        }

        private void DrawModifyControls(SerializedProperty elem, StatusModifyType modifyType)
        {
            DrawSkillSelectors(elem, modifyType);
            DrawStackControls(elem);

            if (modifyType == StatusModifyType.ReplaceStatus)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("statusModifyReplacementSkillID"),
                    new GUIContent("Replacement Skill ID"));
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Target"))
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new GUIContent("Target"));
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"),
                    new GUIContent("Probability (%)"));
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                var conditionProp = elem.FindPropertyRelative("condition");
                EditorGUILayout.PropertyField(conditionProp, new GUIContent("Trigger Condition"));
                FieldVisibilityUI.DrawConditionFields(elem, conditionProp);
            }

            EditorGUILayout.HelpBox("Configure which status skills to adjust and whether they should display stacks. When max stacks is -1 the status can stack infinitely.", MessageType.Info);
        }

        private void DrawSkillSelectors(SerializedProperty elem, StatusModifyType modifyType)
        {
            var skillListProp = elem.FindPropertyRelative("statusModifySkillIDs");
            var legacySkillProp = elem.FindPropertyRelative("statusSkillID");

            if (skillListProp != null)
            {
                EditorGUILayout.PropertyField(skillListProp, new GUIContent("Target Status Skill IDs"), includeChildren: true);
                if (skillListProp.isArray && skillListProp.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("Leave empty to fallback to the legacy Skill ID field.", MessageType.None);
                }
            }

            if (legacySkillProp != null)
            {
                string label = modifyType == StatusModifyType.ApplyStatus
                    ? "Legacy Apply Skill ID"
                    : "Fallback Skill ID";
                EditorGUILayout.PropertyField(legacySkillProp, new GUIContent(label));
            }
        }

        private void DrawStackControls(SerializedProperty elem)
        {
            var showStacksProp = elem.FindPropertyRelative("statusModifyShowStacks");
            if (showStacksProp == null)
                return;

            EditorGUILayout.PropertyField(showStacksProp, new GUIContent("Show Stacks"));
            if (!showStacksProp.boolValue)
                return;

            var stackCountProp = elem.FindPropertyRelative("statusModifyStacks");
            var maxStacksProp = elem.FindPropertyRelative("statusModifyMaxStacks");

            if (stackCountProp != null)
            {
                EditorGUILayout.PropertyField(stackCountProp, new GUIContent("Stack Count"));
                if (stackCountProp.intValue < 0)
                    stackCountProp.intValue = 0;
            }

            if (maxStacksProp != null)
            {
                EditorGUILayout.PropertyField(maxStacksProp, new GUIContent("Max Stacks (-1 = Unlimited)"));
                if (maxStacksProp.intValue < -1)
                    maxStacksProp.intValue = -1;
            }

        }

        private void DrawApplyStackField(SerializedProperty elem)
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