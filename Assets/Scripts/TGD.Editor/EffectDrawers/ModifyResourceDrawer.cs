using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class ModifyResourceDrawer : IEffectDrawer
    {
        // 快捷芯片记忆
        private static int s_AddQuick = +10;
        private static float s_MulQuick = 1.10f;
        private static int s_MaxDeltaQ = +10;

        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Modify Resource", EditorStyles.boldLabel);
            DrawLine();

            var resourceProp = elem.FindPropertyRelative("resourceType");
            var effectTypeProp = elem.FindPropertyRelative("effectType");
            var modifyTypeProp = elem.FindPropertyRelative("resourceModifyType");
            var stateProp = elem.FindPropertyRelative("resourceStateEnabled");

            if (resourceProp != null)
                EditorGUILayout.PropertyField(resourceProp, new GUIContent("Resource Type"));

            bool forceGainMode = effectTypeProp != null &&
                                 (EffectType)effectTypeProp.enumValueIndex == EffectType.GainResource;

            // ModifyType（GainResource 时禁用显示）
            if (modifyTypeProp != null)
            {
                using (new EditorGUI.DisabledScope(forceGainMode))
                {
                    EditorGUILayout.PropertyField(modifyTypeProp, new GUIContent("Modify Type"));
                }
            }

            var modifyType = ResourceModifyType.Gain;
            if (!forceGainMode && modifyTypeProp != null)
                modifyType = (ResourceModifyType)modifyTypeProp.enumValueIndex;

            EditorGUILayout.Space(4);

            bool probabilityHandled = false;

            switch (modifyType)
            {
                case ResourceModifyType.Gain:
                case ResourceModifyType.ConvertMax:
                    probabilityHandled = DrawValueControls(elem, modifyType);
                    break;

                case ResourceModifyType.Lock:
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.LabelField("Lock Resource", EditorStyles.miniBoldLabel);
                    if (stateProp != null)
                        EditorGUILayout.PropertyField(stateProp, new GUIContent("Enabled"));
                    EditorGUILayout.HelpBox("When enabled, the target resource is locked (cannot be spent/changed).", MessageType.Info);
                    EditorGUILayout.EndVertical();
                    break;

                case ResourceModifyType.Overdraft:
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.LabelField("Overdraft", EditorStyles.miniBoldLabel);
                    if (stateProp != null)
                        EditorGUILayout.PropertyField(stateProp, new GUIContent("Enabled"));
                    EditorGUILayout.HelpBox("Allow negative balance (borrowing the resource).", MessageType.Info);
                    EditorGUILayout.EndVertical();
                    break;

                case ResourceModifyType.PayLate:
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.LabelField("Pay Later", EditorStyles.miniBoldLabel);
                    if (stateProp != null)
                        EditorGUILayout.PropertyField(stateProp, new GUIContent("Enabled"));
                    EditorGUILayout.HelpBox("Spend later: defer the cost to a later time or condition.", MessageType.Info);
                    EditorGUILayout.EndVertical();
                    break;
            }

            // 概率
            if (!probabilityHandled)
            {
                if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));
            }

            // Target
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Target"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new GUIContent("Target"));

            // 条件
            var conditionProp = elem.FindPropertyRelative("condition");
            EditorGUILayout.PropertyField(conditionProp, new GUIContent("Trigger Condition"));
            FieldVisibilityUI.DrawConditionFields(elem, conditionProp);
        }

        private bool DrawValueControls(SerializedProperty elem, ResourceModifyType modifyType)
        {
            if (!FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Values"))
                return false;

            bool collapsed;
            if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
            {
                if (!collapsed)
                {
                    var levels = elem.FindPropertyRelative("valueExprLevels");
                    string header = modifyType == ResourceModifyType.ConvertMax
                        ? "Max Delta by Level (e.g. '+10', '*1.1')"
                        : "Value by Level (e.g. '+10', '*1.1', 'max')";
                    PerLevelUI.DrawStringLevels(levels, header);
                }

                int curLv = LevelContext.GetSkillLevel(elem.serializedObject);
                bool showProb = FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
                PerLevelUI.DrawPreviewForCurrentLevel(elem, curLv, showDuration: false, showProb: showProb);
                return false; // 概率未绘制
            }
            else
            {
                var valueProp = FieldVisibilityUI.GetProp(elem, "valueExpression", "value");
                if (valueProp != null)
                {
                    string label = modifyType == ResourceModifyType.ConvertMax
                        ? "Max Delta / Expression"
                        : "Value / Expression";

                    EditorGUILayout.PropertyField(valueProp, new GUIContent(label));

                    // 快捷芯片：Gain → max / ± / × ； ConvertMax → ± / ×
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Quick:", GUILayout.Width(38));
                    if (modifyType == ResourceModifyType.Gain)
                    {
                        if (GUILayout.Button("max", GUILayout.Width(44))) SetExpr(valueProp, "max");
                        s_AddQuick = EditorGUILayout.IntField(s_AddQuick, GUILayout.Width(40));
                        if (GUILayout.Button("+5", GUILayout.Width(36))) SetExpr(valueProp, "+5");
                        if (GUILayout.Button("+10", GUILayout.Width(40))) SetExpr(valueProp, "+10");
                        if (GUILayout.Button("-5", GUILayout.Width(36))) SetExpr(valueProp, "-5");
                        s_MulQuick = EditorGUILayout.FloatField(s_MulQuick, GUILayout.Width(48)); // 1.10
                        if (GUILayout.Button("*1.1", GUILayout.Width(44))) SetExpr(valueProp, "*1.1");
                        if (GUILayout.Button("*0.9", GUILayout.Width(44))) SetExpr(valueProp, "*0.9");
                    }
                    else // ConvertMax
                    {
                        s_MaxDeltaQ = EditorGUILayout.IntField(s_MaxDeltaQ, GUILayout.Width(40));
                        if (GUILayout.Button("+10", GUILayout.Width(40))) SetExpr(valueProp, "+10");
                        if (GUILayout.Button("-10", GUILayout.Width(40))) SetExpr(valueProp, "-10");
                        if (GUILayout.Button("*1.1", GUILayout.Width(44))) SetExpr(valueProp, "*1.1");
                        if (GUILayout.Button("*0.9", GUILayout.Width(44))) SetExpr(valueProp, "*0.9");
                    }
                    if (GUILayout.Button("Clear", GUILayout.Width(50))) SetExpr(valueProp, string.Empty);
                    EditorGUILayout.EndHorizontal();

                    if (modifyType == ResourceModifyType.Gain)
                        EditorGUILayout.HelpBox("Tips: 'max' → 直接回满；前缀 '+/-' → 叠加；前缀 '*' → 按当前值倍率增减。", MessageType.None);
                    else
                        EditorGUILayout.HelpBox("ConvertMax：修改“最大值”。可用 '+/-' 增量或 '*' 按倍率改变上限。", MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox("Missing 'valueExpression' (or legacy 'value') field.", MessageType.Warning);
                }

                if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));

                return true; // 概率已绘制
            }
        }

        private void SetExpr(SerializedProperty prop, string v)
        {
            if (prop == null) return;
            prop.stringValue = v ?? string.Empty;
        }

        private void DrawLine()
        {
            var r = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.25f));
        }
    }
}
