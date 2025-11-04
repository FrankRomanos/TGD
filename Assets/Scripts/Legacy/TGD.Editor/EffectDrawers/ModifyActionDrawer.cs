using System;
using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class ModifyActionDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Modify Action", EditorStyles.boldLabel);
            var skillIdProp = elem.FindPropertyRelative("targetSkillID");
            if (skillIdProp != null)
            {
                EditorGUILayout.PropertyField(skillIdProp, new GUIContent("Modify Skill ID"));
                if (string.IsNullOrWhiteSpace(skillIdProp.stringValue))
                {
                    EditorGUILayout.HelpBox("Leave empty to affect the owning skill.", MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("'targetSkillID' property not found on effect.", MessageType.Warning);
            }

            var targetActionProp = elem.FindPropertyRelative("targetActionType");
            if (targetActionProp != null)
                EditorGUILayout.PropertyField(targetActionProp, new GUIContent("Action Filter"));

            var tagFilterProp = elem.FindPropertyRelative("actionFilterTag");
            if (tagFilterProp != null)
                EditorGUILayout.PropertyField(tagFilterProp, new GUIContent("Skill Tag Filter"));

            var modifyTypeProp = elem.FindPropertyRelative("actionModifyType");
            EditorGUILayout.PropertyField(modifyTypeProp, new GUIContent("Modify Type"));
            var modifyType = (ActionModifyType)modifyTypeProp.enumValueIndex;

            switch (modifyType)
            {
                case ActionModifyType.Damage:
                    DrawDamageBlock(elem);
                    break;
                case ActionModifyType.ActionType:
                    DrawActionTypeBlock(elem);
                    break;
                default:
                    EditorGUILayout.HelpBox("Select a modify type to configure action adjustments.", MessageType.Info);
                    break;
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                DrawCondition(elem);
            }

            DrawSummary(elem, modifyType);
        }

        private void DrawDamageBlock(SerializedProperty elem)
        {
            var modifierProp = elem.FindPropertyRelative("modifierType");
            if (modifierProp != null)
                EditorGUILayout.PropertyField(modifierProp, new GUIContent("Modifier Type"));

            bool drewValue = false;
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Values"))
            {
                bool collapsed;
                if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
                {
                    if (!collapsed)
                    {
                        PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("valueExprLevels"),
                            "Damage Modifier by Level");
                    }

                    int currentLevel = LevelContext.GetSkillLevel(elem.serializedObject);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, currentLevel, showDuration: false, showProb: false);
                    drewValue = true;
                }
            }

            if (!drewValue)
            {
                DrawValueExpression(elem);
            }

            EditorGUILayout.HelpBox("Use expressions like 'p', 'atk * 0.2' or 'p * mastery' to scale the target action damage.", MessageType.None);
        }

        private void DrawValueExpression(SerializedProperty elem)
        {
            var valueProp = FieldVisibilityUI.GetProp(elem, "valueExpression", "value");
            if (valueProp == null)
            {
                EditorGUILayout.HelpBox("Missing valueExpression/value field on EffectDefinition.", MessageType.Warning);
                return;
            }

            EditorGUILayout.PropertyField(valueProp, new GUIContent("Damage Modifier (expression)"));
        }

        private void DrawActionTypeBlock(SerializedProperty elem)
        {
            var overrideProp = elem.FindPropertyRelative("actionTypeOverride");
            if (overrideProp == null)
            {
                EditorGUILayout.HelpBox("'actionTypeOverride' property not found on effect.", MessageType.Warning);
                return;
            }

            EditorGUILayout.PropertyField(overrideProp, new GUIContent("Convert To"));
            EditorGUILayout.HelpBox(
                "All actions matching the selected filter will change to the specified action type while this effect is active.",
                MessageType.Info);
        }

        private void DrawCondition(SerializedProperty elem)
        {
            var condProp = elem.FindPropertyRelative("condition");
            EditorGUILayout.PropertyField(condProp, new GUIContent("Trigger Condition"));

            FieldVisibilityUI.DrawConditionFields(elem, condProp);
        }

        private void DrawSummary(SerializedProperty elem, ActionModifyType type)
        {
            if (type == ActionModifyType.None)
                return;

            var skillIdProp = elem.FindPropertyRelative("targetSkillID");
            string skillId = skillIdProp != null ? skillIdProp.stringValue : string.Empty;
            string skillLabel = string.IsNullOrWhiteSpace(skillId) ? "owning skill" : $"skill '{skillId}'";

            var actionFilterProp = elem.FindPropertyRelative("targetActionType");
            var actionFilter = actionFilterProp != null
                ? (ActionType)actionFilterProp.enumValueIndex
                : ActionType.None;

            string tagFilter = NormalizeTag(elem.FindPropertyRelative("actionFilterTag")?.stringValue);

            switch (type)
            {
                case ActionModifyType.Damage:
                    EditorGUILayout.HelpBox(BuildDamageSummary(elem, actionFilter, skillLabel, tagFilter), MessageType.None);
                    break;
                case ActionModifyType.ActionType:
                    var overrideProp = elem.FindPropertyRelative("actionTypeOverride");
                    var newType = overrideProp != null
                        ? (ActionType)overrideProp.enumValueIndex
                        : ActionType.None;

                    string targetText = BuildTargetDescription(skillLabel, actionFilter, tagFilter);
                    EditorGUILayout.HelpBox($"Convert {targetText} into {DescribeActionFilter(newType, fallback: "the same type")}.", MessageType.None);
                    break;
            }
        }

        private string BuildDamageSummary(SerializedProperty elem, ActionType actionFilter, string skillLabel, string tagFilter)
        {
            string target = BuildTargetDescription(skillLabel, actionFilter, tagFilter);
            var perLevelProp = elem.FindPropertyRelative("perLevel");
            if (perLevelProp != null && perLevelProp.boolValue)
                return $"Adjust {target} using per-level expressions.";

            var valueProp = FieldVisibilityUI.GetProp(elem, "valueExpression", "value");
            if (valueProp == null)
                return $"Adjust {target} (value not set).";

            switch (valueProp.propertyType)
            {
                case SerializedPropertyType.String:
                    return string.IsNullOrWhiteSpace(valueProp.stringValue)
                          ? $"Adjust {target} (value not set)."
                        : $"Adjust {target} by '{valueProp.stringValue}'.";
                case SerializedPropertyType.Float:
                    return $"Adjust {target} by {valueProp.floatValue:0.###}.";
                case SerializedPropertyType.Integer:
                    return $"Adjust {target} by {valueProp.intValue}.";
                default:
                    return $"Adjust {target}.";
            }
        }

        private string BuildTargetDescription(string skillLabel, ActionType actionFilter, string tagFilter)
        {
            tagFilter = NormalizeTag(tagFilter);
            string actionText = DescribeActionFilter(actionFilter, fallback: string.Empty);
            if (!string.IsNullOrWhiteSpace(tagFilter))
            {
                if (string.IsNullOrEmpty(actionText))
                    actionText = $"actions tagged '{tagFilter}'";
                else
                    actionText = $"{actionText} tagged '{tagFilter}'";
            }
            else if (string.IsNullOrEmpty(actionText))
            {
                actionText = "all actions";
            }

            return string.IsNullOrEmpty(actionText)
                ? skillLabel
                : $"{skillLabel} {actionText}";
        }

        private string NormalizeTag(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            string trimmed = value.Trim();
            return string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase) ? string.Empty : trimmed;
        }
        private string DescribeActionFilter(ActionType actionFilter, string fallback = "all actions")
        {
            if (actionFilter == ActionType.None)
                return fallback;
            return actionFilter.ToString().ToLower() + " actions";
        }
    }
}