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
            DrawLine();

            var modifyTypeProp = elem.FindPropertyRelative("statusModifyType");
            EditorGUILayout.PropertyField(modifyTypeProp, new GUIContent("Modify Type"));
            var modifyType = (StatusModifyType)modifyTypeProp.enumValueIndex;

            switch (modifyType)
            {
                case StatusModifyType.ApplyStatus:
                    DrawApplyStatus(elem);
                    break;
                case StatusModifyType.ReplaceStatus:
                case StatusModifyType.DeleteStatus:
                    DrawModifyControls(elem, modifyType);
                    break;
                default:
                    EditorGUILayout.HelpBox("Select a modify type.", MessageType.Info);
                    break;
            }

            // 摘要
            EditorGUILayout.Space(6);
            var summary = BuildSummary(elem, modifyType);
            if (!string.IsNullOrEmpty(summary))
                EditorGUILayout.HelpBox(summary, MessageType.None);
        }

        private void DrawApplyStatus(SerializedProperty elem)
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Apply Status (Buff/Debuff)", EditorStyles.miniBoldLabel);

            DrawSkillSelectors(elem, StatusModifyType.ApplyStatus);

            // 按等级/持续/概率/层数
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

            // 目标
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Target"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new GUIContent("Target"));

            // 条件
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                var conditionProp = elem.FindPropertyRelative("condition");
                EditorGUILayout.PropertyField(conditionProp, new GUIContent("Trigger Condition"));
                FieldVisibilityUI.DrawConditionFields(elem, conditionProp);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawModifyControls(SerializedProperty elem, StatusModifyType modifyType)
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(modifyType == StatusModifyType.ReplaceStatus ? "Replace Status" : "Delete Status",
                EditorStyles.miniBoldLabel);

            DrawSkillSelectors(elem, modifyType);
            DrawStackControls(elem);

            if (modifyType == StatusModifyType.ReplaceStatus)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("statusModifyReplacementSkillID"),
                    new GUIContent("Replacement Skill ID"));
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Target"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new GUIContent("Target"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                var conditionProp = elem.FindPropertyRelative("condition");
                EditorGUILayout.PropertyField(conditionProp, new GUIContent("Trigger Condition"));
                FieldVisibilityUI.DrawConditionFields(elem, conditionProp);
            }

            EditorGUILayout.HelpBox("Configure which status skills to adjust and whether they should display stacks. Max stacks = -1 → unlimited.", MessageType.Info);
            EditorGUILayout.EndVertical();
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
                    EditorGUILayout.HelpBox("Leave empty to fallback to the legacy Skill ID field below.", MessageType.None);
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
                if (stackCountProp.intValue < 0) stackCountProp.intValue = 0;
            }

            if (maxStacksProp != null)
            {
                EditorGUILayout.PropertyField(maxStacksProp, new GUIContent("Max Stacks (-1 = Unlimited)"));
                if (maxStacksProp.intValue < -1) maxStacksProp.intValue = -1;
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
            if (stacksProp.intValue < 1) stacksProp.intValue = 1;
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

        private string BuildSummary(SerializedProperty elem, StatusModifyType modifyType)
        {
            switch (modifyType)
            {
                case StatusModifyType.ApplyStatus:
                    {
                        string sid = GetFirstStatusId(elem);
                        if (string.IsNullOrEmpty(sid)) return string.Empty;

                        int stacks = elem.FindPropertyRelative("stackCount")?.intValue ?? 1;
                        int maxStacks = elem.FindPropertyRelative("maxStacks")?.intValue ?? 0;

                        string dur = ResolveDurationText(elem);
                        string prob = ResolveProbabilityText(elem);

                        return $"Apply '{sid}' {stacks} stack(s){(maxStacks > 0 ? $" (max {maxStacks})" : string.Empty)}{dur}{prob}.";
                    }

                case StatusModifyType.ReplaceStatus:
                    {
                        string list = GetStatusList(elem);
                        string rep = elem.FindPropertyRelative("statusModifyReplacementSkillID")?.stringValue ?? "";
                        if (string.IsNullOrEmpty(list) || string.IsNullOrEmpty(rep)) return string.Empty;
                        return $"Replace [{list}] → '{rep}'.";
                    }

                case StatusModifyType.DeleteStatus:
                    {
                        string list = GetStatusList(elem);
                        if (string.IsNullOrEmpty(list)) return string.Empty;
                        return $"Remove statuses: [{list}].";
                    }
            }
            return string.Empty;
        }

        private string ResolveDurationText(SerializedProperty elem)
        {
            bool perLevel = elem.FindPropertyRelative("perLevel")?.boolValue ?? false;
            if (perLevel)
                return " (duration per-level)";
            var durProp = elem.FindPropertyRelative("duration");
            if (durProp == null) return string.Empty;

            int dur = durProp.propertyType == SerializedPropertyType.Integer
                ? durProp.intValue
                : Mathf.RoundToInt(durProp.floatValue);
            if (dur == 0) return string.Empty;
            if (dur == -1) return " (instant)";
            if (dur == -2) return " (permanent)";
            return $" (duration {dur} turns)";
        }

        private string ResolveProbabilityText(SerializedProperty elem)
        {
            bool hasProb = FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
            if (!hasProb) return string.Empty;

            bool perLevel = elem.FindPropertyRelative("perLevel")?.boolValue ?? false;
            if (perLevel) return " (probability per-level)";

            var p = elem.FindPropertyRelative("probability");
            if (p == null) return string.Empty;
            var s = p.stringValue;
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            return $" (prob {s})";
        }

        private string GetFirstStatusId(SerializedProperty elem)
        {
            var list = elem.FindPropertyRelative("statusModifySkillIDs");
            if (list != null && list.isArray && list.arraySize > 0)
                return list.GetArrayElementAtIndex(0).stringValue;

            var legacy = elem.FindPropertyRelative("statusSkillID");
            return legacy != null ? legacy.stringValue : string.Empty;
        }

        private string GetStatusList(SerializedProperty elem)
        {
            var list = elem.FindPropertyRelative("statusModifySkillIDs");
            if (list != null && list.isArray && list.arraySize > 0)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < list.arraySize; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(list.GetArrayElementAtIndex(i).stringValue);
                }
                return sb.ToString();
            }
            var legacy = elem.FindPropertyRelative("statusSkillID");
            return legacy != null ? legacy.stringValue : string.Empty;
        }

        private void DrawLine()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.25f));
        }
    }
}
