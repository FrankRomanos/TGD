using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>
    /// Inspector drawer for EffectType.ModifySkill.
    /// Unified workflow to adjust different skill properties with quick actions.
    /// </summary>
    public class ModifySkillDrawer : IEffectDrawer
    {
        // 记忆一些小快捷输入
        private static int s_CdQuick = -6;
        private static int s_TimeQuick = -1;
        private static int s_RangeQuick = +1;
        private static int s_CostQuick = +5;

        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Modify Skill", EditorStyles.boldLabel);
            DrawLine();

            // 选择目标技能
            var skillIdProp = elem.FindPropertyRelative("targetSkillID");
            EditorGUILayout.PropertyField(skillIdProp, new GUIContent("Modify Skill ID"));
            if (string.IsNullOrWhiteSpace(skillIdProp.stringValue))
            {
                EditorGUILayout.HelpBox("Skill ID is required for Modify Skill effects.", MessageType.Warning);
            }

            // 修改类型
            var modifyTypeProp = elem.FindPropertyRelative("skillModifyType");
            EditorGUILayout.PropertyField(modifyTypeProp, new GUIContent("Modify Type"));
            var modifyType = (SkillModifyType)modifyTypeProp.enumValueIndex;

            // 操作符（部分类型用不到）
            if (modifyType != SkillModifyType.None &&
                modifyType != SkillModifyType.CooldownReset &&
                modifyType != SkillModifyType.AddCost &&
                modifyType != SkillModifyType.ForbidUse)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("skillModifyOperation"),
                    new GUIContent("Operation"));
            }

            // 值分区
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

            // 限制
            DrawLimitControls(elem, modifyType);

            // 触发条件
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                DrawCondition(elem);
            }

            // 摘要
            DrawSummary(elem, modifyType);
        }

        private void DrawValueBlock(SerializedProperty elem, SkillModifyType type)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Value", EditorStyles.miniBoldLabel);

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

            // 每级 or 单值
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
                    DrawQuickChips(elem, type);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawQuickChips(SerializedProperty elem, SkillModifyType type)
        {
            // 根据类型给些常用“芯片按钮”
            var valProp = FieldVisibilityUI.GetProp(elem, "valueExpression", "value");
            if (valProp == null) return;

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Quick:", GUILayout.Width(38));

            switch (type)
            {
                case SkillModifyType.CooldownModify:
                    s_CdQuick = EditorGUILayout.IntField(s_CdQuick, GUILayout.Width(36));
                    if (GUILayout.Button("-12", GUILayout.Width(36))) SetExpr(valProp, "-12");
                    if (GUILayout.Button("-6", GUILayout.Width(36))) SetExpr(valProp, "-6");
                    if (GUILayout.Button("+6", GUILayout.Width(36))) SetExpr(valProp, "+6");
                    if (GUILayout.Button("+12", GUILayout.Width(36))) SetExpr(valProp, "+12");
                    break;

                case SkillModifyType.TimeCost:
                    s_TimeQuick = EditorGUILayout.IntField(s_TimeQuick, GUILayout.Width(36));
                    if (GUILayout.Button("-2", GUILayout.Width(36))) SetExpr(valProp, "-2");
                    if (GUILayout.Button("-1", GUILayout.Width(36))) SetExpr(valProp, "-1");
                    if (GUILayout.Button("+1", GUILayout.Width(36))) SetExpr(valProp, "+1");
                    if (GUILayout.Button("+2", GUILayout.Width(36))) SetExpr(valProp, "+2");
                    break;

                case SkillModifyType.Range:
                    s_RangeQuick = EditorGUILayout.IntField(s_RangeQuick, GUILayout.Width(36));
                    if (GUILayout.Button("-2", GUILayout.Width(36))) SetExpr(valProp, "-2");
                    if (GUILayout.Button("-1", GUILayout.Width(36))) SetExpr(valProp, "-1");
                    if (GUILayout.Button("+1", GUILayout.Width(36))) SetExpr(valProp, "+1");
                    if (GUILayout.Button("+2", GUILayout.Width(36))) SetExpr(valProp, "+2");
                    break;

                case SkillModifyType.ResourceCost:
                case SkillModifyType.AddCost:
                    s_CostQuick = EditorGUILayout.IntField(s_CostQuick, GUILayout.Width(36));
                    if (GUILayout.Button("+5", GUILayout.Width(36))) SetExpr(valProp, "+5");
                    if (GUILayout.Button("+10", GUILayout.Width(36))) SetExpr(valProp, "+10");
                    if (GUILayout.Button("*0.9", GUILayout.Width(44))) SetExpr(valProp, "*0.9");
                    if (GUILayout.Button("*1.1", GUILayout.Width(44))) SetExpr(valProp, "*1.1");
                    break;

                case SkillModifyType.Damage:
                case SkillModifyType.Heal:
                case SkillModifyType.BuffPower:
                    if (GUILayout.Button("*1.1", GUILayout.Width(44))) SetExpr(valProp, "*1.1");
                    if (GUILayout.Button("*1.2", GUILayout.Width(44))) SetExpr(valProp, "*1.2");
                    if (GUILayout.Button("*0.8", GUILayout.Width(44))) SetExpr(valProp, "*0.8");
                    if (GUILayout.Button("+10%", GUILayout.Width(52))) SetExpr(valProp, "*1.1");
                    break;

                case SkillModifyType.Duration:
                    if (GUILayout.Button("+1 turn", GUILayout.Width(70))) SetExpr(valProp, "+1");
                    if (GUILayout.Button("+2", GUILayout.Width(36))) SetExpr(valProp, "+2");
                    if (GUILayout.Button("-1", GUILayout.Width(36))) SetExpr(valProp, "-1");
                    break;
            }

            if (GUILayout.Button("Clear", GUILayout.Width(50))) SetExpr(valProp, string.Empty);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLimitControls(SerializedProperty elem, SkillModifyType type)
        {
            if (type == SkillModifyType.None || type == SkillModifyType.CooldownReset)
                return;

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Limit (optional)", EditorStyles.miniBoldLabel);

            var enabledProp = elem.FindPropertyRelative("modifyLimitEnabled");
            if (enabledProp == null)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.PropertyField(enabledProp, new GUIContent("Enable Limit"));
            if (enabledProp.boolValue)
            {
                var limitExprProp = elem.FindPropertyRelative("modifyLimitExpression");
                var limitValueProp = elem.FindPropertyRelative("modifyLimitValue");
                if (limitExprProp != null)
                    EditorGUILayout.PropertyField(limitExprProp, new GUIContent("Limit Expression"));
                if (limitValueProp != null)
                    EditorGUILayout.PropertyField(limitValueProp, new GUIContent("Limit (Fallback)"));

                EditorGUILayout.HelpBox("Limits cap how far the modification can adjust the value.", MessageType.None);
            }
            EditorGUILayout.EndVertical();
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
                    EditorGUILayout.HelpBox("Positive values extend cooldown. Negative values reduce cooldown.", MessageType.Info);
                    break;
                case SkillModifyType.TimeCost:
                    EditorGUILayout.HelpBox("Time cost is measured in seconds. Use negative values to reduce it.", MessageType.Info);
                    break;
                case SkillModifyType.Range:
                    EditorGUILayout.HelpBox("Flat values move range in tiles. Percentage values scale the current range.", MessageType.Info);
                    break;
                case SkillModifyType.ResourceCost:
                    EditorGUILayout.HelpBox("Flat values change resource amount. Percentage values scale the original cost.", MessageType.Info);
                    break;
                case SkillModifyType.AddCost:
                    EditorGUILayout.HelpBox("Adds a new cost requirement to the skill. Use expressions for dynamic values.", MessageType.Info);
                    break;
                case SkillModifyType.Duration:
                    EditorGUILayout.HelpBox("Duration changes are measured in turns. Use negative values to shorten effects.", MessageType.Info);
                    break;
                case SkillModifyType.BuffPower:
                    EditorGUILayout.HelpBox("Scales healing and buff outputs produced by the target skill.", MessageType.Info);
                    break;
            }
        }

        private void DrawResetBlock(SerializedProperty elem)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Cooldown Reset", EditorStyles.miniBoldLabel);

            var resetProp = elem.FindPropertyRelative("resetCooldownToMax");
            EditorGUILayout.PropertyField(resetProp, new GUIContent("Refresh Cooldown"));
            EditorGUILayout.HelpBox(
                resetProp.boolValue
                    ? "When enabled the skill starts a brand new cooldown cycle."
                    : "When disabled the remaining cooldown is cleared but a new cooldown does not start.",
                MessageType.Info);

            EditorGUILayout.EndVertical();
        }

        private void DrawCondition(SerializedProperty elem)
        {
            var condProp = elem.FindPropertyRelative("condition");
            EditorGUILayout.PropertyField(condProp, new GUIContent("Trigger Condition"));
            FieldVisibilityUI.DrawConditionFields(elem, condProp);
        }

        private void DrawSummary(SerializedProperty elem, SkillModifyType type)
        {
            EditorGUILayout.Space(6);
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
            string limitSuffix = GetLimitSummarySuffix(elem);
            if (!string.IsNullOrEmpty(limitSuffix))
                limitSuffix = " " + limitSuffix;

            if (type == SkillModifyType.CooldownReset)
            {
                bool refresh = elem.FindPropertyRelative("resetCooldownToMax").boolValue;
                return refresh
                    ? $"Reset cooldown of '{skillId}' and start a fresh cooldown."
                    : $"Clear the remaining cooldown of '{skillId}' without starting a new cooldown.";
            }
            if (type == SkillModifyType.ForbidUse)
                return $"Disable '{skillId}'{limitSuffix}.";

            var opProp = elem.FindPropertyRelative("skillModifyOperation");
            SkillModifyOperation op = opProp != null
                ? (SkillModifyOperation)opProp.enumValueIndex
                : SkillModifyOperation.Minus;

            (string verb, string connector) = GetOperationWords(op);
            string valueText = GetSummaryValue(elem);

            switch (type)
            {
                case SkillModifyType.CooldownModify:
                    return $"{verb} cooldown of '{skillId}' {connector} {valueText}{limitSuffix}.";
                case SkillModifyType.Range:
                    return $"{verb} range of '{skillId}' {connector} {valueText}{limitSuffix}.";
                case SkillModifyType.TimeCost:
                    return $"{verb} time cost of '{skillId}' {connector} {valueText}{limitSuffix}.";
                case SkillModifyType.Damage:
                    return $"{verb} damage of '{skillId}' {connector} {valueText}{limitSuffix}.";
                case SkillModifyType.Heal:
                    return $"{verb} healing of '{skillId}' {connector} {valueText}{limitSuffix}.";
                case SkillModifyType.ResourceCost:
                    bool affectsAll = elem.FindPropertyRelative("modifyAffectsAllCosts").boolValue;
                    string target = affectsAll
                        ? "all costs"
                        : $"{((CostResourceType)elem.FindPropertyRelative("modifyCostResource").enumValueIndex)} cost";
                    return $"{verb} {target} for '{skillId}' {connector} {valueText}{limitSuffix}.";
                case SkillModifyType.Duration:
                    return $"{verb} duration of '{skillId}' {connector} {valueText}{limitSuffix}.";
                case SkillModifyType.BuffPower:
                    return $"{verb} buff potency of '{skillId}' {connector} {valueText}{limitSuffix}.";
                default:
                    return string.Empty;
            }
        }

        private string GetLimitSummarySuffix(SerializedProperty elem)
        {
            var enabledProp = elem.FindPropertyRelative("modifyLimitEnabled");
            if (enabledProp == null || !enabledProp.boolValue)
                return string.Empty;

            var exprProp = elem.FindPropertyRelative("modifyLimitExpression");
            if (exprProp != null && !string.IsNullOrWhiteSpace(exprProp.stringValue))
                return $"(limit: {exprProp.stringValue})";

            var valueProp = elem.FindPropertyRelative("modifyLimitValue");
            if (valueProp != null)
            {
                switch (valueProp.propertyType)
                {
                    case SerializedPropertyType.Float: return $"(limit: {valueProp.floatValue:0.###})";
                    case SerializedPropertyType.Integer: return $"(limit: {valueProp.intValue})";
                    case SerializedPropertyType.String:
                        if (!string.IsNullOrWhiteSpace(valueProp.stringValue)) return $"(limit: {valueProp.stringValue})";
                        break;
                }
            }
            return "(limit enabled)";
        }

        private (string verb, string connector) GetOperationWords(SkillModifyOperation op)
        {
            switch (op)
            {
                case SkillModifyOperation.Override: return ("Set", "to");
                case SkillModifyOperation.Multiply: return ("Scale", "by");
                default: return ("Reduce", "by");
            }
        }

        private string GetSummaryValue(SerializedProperty elem)
        {
            var perLevelProp = elem.FindPropertyRelative("perLevel");
            if (perLevelProp != null && perLevelProp.boolValue)
                return "per-level values";

            var valueProp = FieldVisibilityUI.GetProp(elem, "valueExpression", "value");
            if (valueProp == null) return "(value not set)";

            switch (valueProp.propertyType)
            {
                case SerializedPropertyType.String:
                    return string.IsNullOrWhiteSpace(valueProp.stringValue)
                        ? "(value not set)"
                        : $"'{valueProp.stringValue}'";
                case SerializedPropertyType.Float: return valueProp.floatValue.ToString("0.###");
                case SerializedPropertyType.Integer: return valueProp.intValue.ToString();
                default: return valueProp.ToString();
            }
        }

        private string GetValueLabel(SkillModifyType type)
        {
            switch (type)
            {
                case SkillModifyType.CooldownModify: return "Cooldown Change (seconds / expression)";
                case SkillModifyType.Range: return "Range Change / Expression";
                case SkillModifyType.TimeCost: return "Time Cost Change (seconds / expression)";
                case SkillModifyType.Damage: return "Damage Modifier (expression)";
                case SkillModifyType.Heal: return "Heal Modifier (expression)";
                case SkillModifyType.ResourceCost: return "Cost Change (expression)";
                case SkillModifyType.AddCost: return "Additional Cost (expression)";
                case SkillModifyType.Duration: return "Duration Change (turns / expression)";
                case SkillModifyType.BuffPower: return "Buff Power Modifier (expression)";
                default: return "Value / Expression";
            }
        }

        private void SetExpr(SerializedProperty prop, string v)
        {
            if (prop == null) return;
            if (prop.propertyType == SerializedPropertyType.String) prop.stringValue = v;
            else if (prop.propertyType == SerializedPropertyType.Integer && int.TryParse(v, out var iv)) prop.intValue = iv;
            else if (prop.propertyType == SerializedPropertyType.Float && float.TryParse(v, out var fv)) prop.floatValue = fv;
        }

        private void DrawLine()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.25f));
        }
    }
}
