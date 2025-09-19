using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>
    /// Inspector drawer for EffectType.ModifySkill.
    /// Provides a unified workflow to adjust different skill properties (range, cooldown, damage, etc.).
    /// </summary>
    public class ModifySkillDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Modify Skill", EditorStyles.boldLabel);

            var skillIdProp = elem.FindPropertyRelative("targetSkillID");
            EditorGUILayout.PropertyField(skillIdProp, new GUIContent("Modify Skill ID"));
            if (string.IsNullOrWhiteSpace(skillIdProp.stringValue))
            {
                EditorGUILayout.HelpBox("Skill ID is required for Modify Skill effects.", MessageType.Warning);
            }

            var modifyTypeProp = elem.FindPropertyRelative("skillModifyType");
            EditorGUILayout.PropertyField(modifyTypeProp, new GUIContent("Modify Type"));
            var modifyType = (SkillModifyType)modifyTypeProp.enumValueIndex;

            if (modifyType != SkillModifyType.None && modifyType != SkillModifyType.CooldownReset &&
            modifyType != SkillModifyType.AddCost && modifyType != SkillModifyType.ForbidUse)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("skillModifyOperation"),
                    new GUIContent("Operation"));
            }

            switch (modifyType)
            {
                case SkillModifyType.Range:
                case SkillModifyType.TimeCost:
                case SkillModifyType.CooldownModify:
                case SkillModifyType.Damage:
                case SkillModifyType.Heal:
                case SkillModifyType.ResourceCost:
                case SkillModifyType.AddCost:
                case SkillModifyType.Duration:
                case SkillModifyType.BuffPower:
                    DrawValueBlock(elem, modifyType);
                    break;
                case SkillModifyType.CooldownReset:
                    DrawResetBlock(elem);
                    break;
                case SkillModifyType.ForbidUse:
                    EditorGUILayout.HelpBox("Prevents the selected skill from being used.", MessageType.Info);
                    break;
                default:
                    EditorGUILayout.HelpBox("Select a modify type to configure detailed values.", MessageType.Info);
                    break;
            }
            DrawLimitControls(elem, modifyType);

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                DrawCondition(elem);
            }

            DrawSummary(elem, modifyType);
        }

        private void DrawValueBlock(SerializedProperty elem, SkillModifyType type)
        {
            bool showModifierType = type == SkillModifyType.Range ||
                                    type == SkillModifyType.TimeCost ||
                                    type == SkillModifyType.Damage ||
                                    type == SkillModifyType.Heal ||
                                    type == SkillModifyType.ResourceCost ||
                                    type == SkillModifyType.CooldownModify ||
                                    type == SkillModifyType.Duration ||
                                    type == SkillModifyType.BuffPower;

            if (showModifierType)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("modifierType"),
                    new GUIContent("Modifier Type"));
            }

            if (type == SkillModifyType.ResourceCost)
            {
                var affectsAllProp = elem.FindPropertyRelative("modifyAffectsAllCosts");
                EditorGUILayout.PropertyField(affectsAllProp, new GUIContent("Affect All Costs"));
                if (!affectsAllProp.boolValue)
                {
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("modifyCostResource"),
                        new GUIContent("Target Resource"));
                }
            }
            else if (type == SkillModifyType.AddCost)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("modifyCostResource"),
                    new GUIContent("Cost Resource"));
            }
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Values"))
            {
                bool collapsed;
                if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
                {
                    if (!collapsed)
                    {
                        PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("valueExprLevels"),
                            "Value Expression by Level");
                    }

                    int currentLevel = LevelContext.GetSkillLevel(elem.serializedObject);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, currentLevel, showDuration: false, showProb: false);
                }
                else
                {
                    DrawValueField(elem, type);
                }
            }
        }

        private void DrawLimitControls(SerializedProperty elem, SkillModifyType type)
        {
            if (type == SkillModifyType.None || type == SkillModifyType.CooldownReset)

             return;
            var enabledProp = elem.FindPropertyRelative("modifyLimitEnabled");
            if (enabledProp == null)
                return;
            EditorGUILayout.PropertyField(enabledProp, new GUIContent("Show Limit"));
            if (!enabledProp.boolValue)
                return;
            var limitExprProp = elem.FindPropertyRelative("modifyLimitExpression");
            var limitValueProp = elem.FindPropertyRelative("modifyLimitValue");
            if (limitExprProp != null)
                EditorGUILayout.PropertyField(limitExprProp, new GUIContent("Limit Expression"));
            if (limitValueProp != null)
                EditorGUILayout.PropertyField(limitValueProp, new GUIContent("Limit (Fallback)"));

            EditorGUILayout.HelpBox("Limits cap how far the modification can adjust the value.", MessageType.None);
        }
        private void DrawValueField(SerializedProperty elem, SkillModifyType type)
        {
            var valueProp = FieldVisibilityUI.GetProp(elem, "valueExpression", "value");
            if (valueProp == null)
            {
                EditorGUILayout.HelpBox("Missing valueExpression/value field on EffectDefinition.", MessageType.Warning);
                return;
            }

            EditorGUILayout.PropertyField(valueProp, new GUIContent(GetValueLabel(type)));

            switch (type)
            {
                case SkillModifyType.CooldownReset:
                    bool refresh = elem.FindPropertyRelative("resetCooldownToMax").boolValue;
                    return refresh
                        ? $"Reset cooldown of '{skillId}' and start a fresh cooldown."
                        : $"Clear the remaining cooldown of '{skillId}' without starting a new cooldown.";
                case SkillModifyType.CooldownModify:
                    return $"{verb} cooldown of '{skillId}' {connector} {valueText}{limitText}.";
                case SkillModifyType.Range:
                    return $"{verb} range of '{skillId}' {connector} {valueText}{limitText}.";
                case SkillModifyType.TimeCost:
                    return $"{verb} time cost of '{skillId}' {connector} {valueText}{limitText}.";
                case SkillModifyType.Damage:
                    return $"{verb} damage of '{skillId}' {connector} {valueText}{limitText}.";
                case SkillModifyType.Heal:
                    return $"{verb} healing of '{skillId}' {connector} {valueText}{limitText}.";
                case SkillModifyType.ResourceCost:
                    bool affectsAll = elem.FindPropertyRelative("modifyAffectsAllCosts").boolValue;
                    string target = affectsAll
                        ? "all costs"
                        : $"{((CostResourceType)elem.FindPropertyRelative("modifyCostResource").enumValueIndex)} cost";
                    return $"{verb} {target} for '{skillId}' {connector} {valueText}{limitText}.";
                case SkillModifyType.AddCost:
                    var costResourceProp = elem.FindPropertyRelative("modifyCostResource");
                    string resourceLabel = costResourceProp != null
                        ? ((CostResourceType)costResourceProp.enumValueIndex).ToString()
                        : "resource";
                    return $"Add {resourceLabel} cost '{valueText}' to '{skillId}'{limitText}.";
                case SkillModifyType.Duration:
                    return $"{verb} duration of '{skillId}' {connector} {valueText}{limitText}.";
                case SkillModifyType.BuffPower:
                    return $"{verb} buff potency of '{skillId}' {connector} {valueText}{limitText}.";
                case SkillModifyType.ForbidUse:
                    return $"Disable '{skillId}'{limitText}.";
                default:
                    return string.Empty;
            }

        }

        private (string verb, string connector) GetOperationWords(SkillModifyOperation op)
        {
            switch (op)
            {
                case SkillModifyOperation.Override:
                    return ("Set", "to");
                case SkillModifyOperation.Multiply:
                    return ("Scale", "by");
                default:
                    return ("Reduce", "by");
            }
        }

        private string GetSummaryValue(SerializedProperty elem)
        {
            var perLevelProp = elem.FindPropertyRelative("perLevel");
            if (perLevelProp != null && perLevelProp.boolValue)
            {
                return "per-level values";
            }

            var valueProp = FieldVisibilityUI.GetProp(elem, "valueExpression", "value");
            if (valueProp == null)
                return "(value not set)";

            switch (valueProp.propertyType)
            {
                case SerializedPropertyType.String:
                    return string.IsNullOrWhiteSpace(valueProp.stringValue)
                        ? "(value not set)"
                        : $"'{valueProp.stringValue}'";
                case SerializedPropertyType.Float:
                    return valueProp.floatValue.ToString("0.###");
                case SerializedPropertyType.Integer:
                    return valueProp.intValue.ToString();
                default:
                    return valueProp.ToString();
            }
        }

        private string GetValueLabel(SkillModifyType type)
        {
            switch (type)
            {
                case SkillModifyType.CooldownModify:
                    return "Cooldown Change (seconds / expression)";
                case SkillModifyType.Range:
                    return "Range Change / Expression";
                case SkillModifyType.TimeCost:
                    return "Time Cost Change (seconds / expression)";
                case SkillModifyType.Damage:
                    return "Damage Modifier (expression)";
                case SkillModifyType.Heal:
                    return "Heal Modifier (expression)";
                case SkillModifyType.ResourceCost:
                    return "Cost Change (expression)";
                case SkillModifyType.AddCost:
                    return "Additional Cost (expression)";
                case SkillModifyType.Duration:
                    return "Duration Change (turns / expression)";
                case SkillModifyType.BuffPower:
                    return "Buff Power Modifier (expression)";
                default:
                    return "Value / Expression";
            }
        }
    }
}