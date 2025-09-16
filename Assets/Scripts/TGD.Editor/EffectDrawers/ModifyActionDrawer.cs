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

            if ((EffectCondition)condProp.enumValueIndex == EffectCondition.OnNextSkillSpendResource)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("conditionResourceType"),
                    new GUIContent("Cond. Resource"));
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("conditionMinAmount"),
                    new GUIContent("Min Spend"));
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("consumeStatusOnTrigger"),
                    new GUIContent("Consume Status On Trigger"));
            }
        }

        private void DrawSummary(SerializedProperty elem, ActionModifyType type)
        {
            if (type == ActionModifyType.None)
                return;

            var actionFilterProp = elem.FindPropertyRelative("targetActionType");
            var actionFilter = actionFilterProp != null
                ? (ActionType)actionFilterProp.enumValueIndex
                : ActionType.None;

            switch (type)
            {
                case ActionModifyType.Damage:
                    EditorGUILayout.HelpBox(BuildDamageSummary(elem, actionFilter), MessageType.None);
                    break;
                case ActionModifyType.ActionType:
                    var overrideProp = elem.FindPropertyRelative("actionTypeOverride");
                    var newType = overrideProp != null
                        ? (ActionType)overrideProp.enumValueIndex
                        : ActionType.None;
                    EditorGUILayout.HelpBox($"Convert {actionFilter} actions into {newType} actions.", MessageType.None);
                    break;
            }
        }

        private string BuildDamageSummary(SerializedProperty elem, ActionType actionFilter)
        {
            var perLevelProp = elem.FindPropertyRelative("perLevel");
            if (perLevelProp != null && perLevelProp.boolValue)
                return $"Adjust {actionFilter} action damage using per-level expressions.";

            var valueProp = FieldVisibilityUI.GetProp(elem, "valueExpression", "value");
            if (valueProp == null)
                return $"Adjust {actionFilter} action damage (value not set).";

            switch (valueProp.propertyType)
            {
                case SerializedPropertyType.String:
                    return string.IsNullOrWhiteSpace(valueProp.stringValue)
                        ? $"Adjust {actionFilter} action damage (value not set)."
                        : $"Adjust {actionFilter} action damage by '{valueProp.stringValue}'.";
                case SerializedPropertyType.Float:
                    return $"Adjust {actionFilter} action damage by {valueProp.floatValue:0.###}.";
                case SerializedPropertyType.Integer:
                    return $"Adjust {actionFilter} action damage by {valueProp.intValue}.";
                default:
                    return $"Adjust {actionFilter} action damage.";
            }
        }
    }
}