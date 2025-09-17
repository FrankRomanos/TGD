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

            if (modifyType != SkillModifyType.None && modifyType != SkillModifyType.CooldownReset)
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
                    DrawValueBlock(elem, modifyType);
                    break;
                case SkillModifyType.CooldownReset:
                    DrawResetBlock(elem);
                    break;
                default:
                    EditorGUILayout.HelpBox("Select a modify type to configure detailed values.", MessageType.Info);
                    break;
            }

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
                                    type == SkillModifyType.CooldownModify;

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
                case SkillModifyType.CooldownModify:
                    EditorGUILayout.HelpBox("Positive values extend cooldown. Negative values reduce cooldown.",
                        MessageType.Info);
                    break;
                case SkillModifyType.TimeCost:
                    EditorGUILayout.HelpBox("Time cost is measured in seconds. Use negative values to reduce it.",
                        MessageType.Info);
                    break;
                case SkillModifyType.Range:
                    EditorGUILayout.HelpBox("Flat values move range in tiles. Percentage values scale the current range.",
                        MessageType.Info);
                    break;
                case SkillModifyType.ResourceCost:
                    EditorGUILayout.HelpBox("Flat values change resource amount. Percentage values scale the original cost.",
                        MessageType.Info);
                    break;
            }
        }

        private void DrawResetBlock(SerializedProperty elem)
        {
            var resetProp = elem.FindPropertyRelative("resetCooldownToMax");
            EditorGUILayout.PropertyField(resetProp, new GUIContent("Refresh Cooldown"));
            EditorGUILayout.HelpBox(
                resetProp.boolValue
                    ? "When enabled the skill starts a brand new cooldown cycle."
                    : "When disabled the remaining cooldown is cleared but a new cooldown does not start.",
                MessageType.Info);
        }

        private void DrawCondition(SerializedProperty elem)
        {
            var condProp = elem.FindPropertyRelative("condition");
            EditorGUILayout.PropertyField(condProp, new GUIContent("Trigger Condition"));

            FieldVisibilityUI.DrawConditionFields(elem, condProp);
        }

        private void DrawSummary(SerializedProperty elem, SkillModifyType type)
        {
            var skillId = elem.FindPropertyRelative("targetSkillID").stringValue;
            if (string.IsNullOrEmpty(skillId) || type == SkillModifyType.None)
                return;

            string summary = BuildSummary(elem, type, skillId);
            if (!string.IsNullOrEmpty(summary))
            {
                EditorGUILayout.HelpBox(summary, MessageType.None);
            }
        }

        private string BuildSummary(SerializedProperty elem, SkillModifyType type, string skillId)
        {
            var opProp = elem.FindPropertyRelative("skillModifyOperation");
            SkillModifyOperation op = opProp != null ? (SkillModifyOperation)opProp.enumValueIndex : SkillModifyOperation.Add;
            (string verb, string connector) = GetOperationWords(op);
            string valueText = GetSummaryValue(elem);

            switch (type)
            {
                case SkillModifyType.CooldownReset:
                    bool refresh = elem.FindPropertyRelative("resetCooldownToMax").boolValue;
                    return refresh
                        ? $"Reset cooldown of '{skillId}' and start a fresh cooldown."
                        : $"Clear the remaining cooldown of '{skillId}' without starting a new cooldown.";
                case SkillModifyType.CooldownModify:
                    return $"{verb} cooldown of '{skillId}' {connector} {valueText}.";
                case SkillModifyType.Range:
                    return $"{verb} range of '{skillId}' {connector} {valueText}.";
                case SkillModifyType.TimeCost:
                    return $"{verb} time cost of '{skillId}' {connector} {valueText}.";
                case SkillModifyType.Damage:
                    return $"{verb} damage of '{skillId}' {connector} {valueText}.";
                case SkillModifyType.Heal:
                    return $"{verb} healing of '{skillId}' {connector} {valueText}.";
                case SkillModifyType.ResourceCost:
                    bool affectsAll = elem.FindPropertyRelative("modifyAffectsAllCosts").boolValue;
                    string target = affectsAll
                        ? "all costs"
                        : $"{((CostResourceType)elem.FindPropertyRelative("modifyCostResource").enumValueIndex)} cost";
                    return $"{verb} {target} for '{skillId}' {connector} {valueText}.";
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
                    return ("Adjust", "by");
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
                default:
                    return "Value / Expression";
            }
        }
    }
}