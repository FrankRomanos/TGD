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

            switch (type)
            {
                case ActionModifyType.Damage:
                    EditorGUILayout.HelpBox(BuildDamageSummary(elem, actionFilter, skillLabel), MessageType.None);
                    break;
                case ActionModifyType.ActionType:
                    var overrideProp = elem.FindPropertyRelative("actionTypeOverride");
                    var newType = overrideProp != null
                        ? (ActionType)overrideProp.enumValueIndex
                        : ActionType.None;

                    string actionText = DescribeActionFilter(actionFilter);
                    string targetText = string.IsNullOrEmpty(actionText) ? skillLabel : $"{skillLabel} {actionText}";
                    EditorGUILayout.HelpBox($"Convert {targetText} into {DescribeActionFilter(newType, fallback: "the same type")}.", MessageType.None);
                    break;
            }
        }

        private string BuildDamageSummary(SerializedProperty elem, ActionType actionFilter, string skillLabel)
        {
            var perLevelProp = elem.FindPropertyRelative("perLevel");
            if (perLevelProp != null && perLevelProp.boolValue)
                return $"Adjust {skillLabel} {DescribeActionFilter(actionFilter)} using per-level expressions.";

            var valueProp = FieldVisibilityUI.GetProp(elem, "valueExpression", "value");
            if (valueProp == null)
                return $"Adjust {skillLabel} {DescribeActionFilter(actionFilter)} (value not set).";

            switch (valueProp.propertyType)
            {
                case SerializedPropertyType.String:
                    return string.IsNullOrWhiteSpace(valueProp.stringValue)
                        ? $"Adjust {skillLabel} {DescribeActionFilter(actionFilter)} (value not set)."
                        : $"Adjust {skillLabel} {DescribeActionFilter(actionFilter)} by '{valueProp.stringValue}'.";
                case SerializedPropertyType.Float:
                    return $"Adjust {skillLabel} {DescribeActionFilter(actionFilter)} by {valueProp.floatValue:0.###}.";
                case SerializedPropertyType.Integer:
                    return $"Adjust {skillLabel} {DescribeActionFilter(actionFilter)} by {valueProp.intValue}.";
                default:
                    return $"Adjust {skillLabel} {DescribeActionFilter(actionFilter)}.";
            }
        }
        private string DescribeActionFilter(ActionType actionFilter, string fallback = "all actions")
        {
            if (actionFilter == ActionType.None)
                return fallback;
            return actionFilter.ToString().ToLower() + " actions";
        }
    }
}