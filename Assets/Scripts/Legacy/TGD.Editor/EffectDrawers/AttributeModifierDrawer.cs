using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class AttributeModifierDrawer : IEffectDrawer
    {
        // 记忆一组常用预设（百分比/平铺）
        private static float s_QuickPct = 0.10f;  // 10%
        private static float s_QuickFlat = 10f;

        // per-level 快速阶梯（多数属性走等差更直观）
        private static float s_Base = 10f;
        private static float s_Step = 5f;
        private static bool s_PercentageModePreview = true;

        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Attribute Modifier", EditorStyles.boldLabel);
            DrawLine();

            var attrProp = elem.FindPropertyRelative("attributeType");
            var modProp = elem.FindPropertyRelative("modifierType");
            var tgtProp = elem.FindPropertyRelative("target");

            // Attribute 先选
            EditorGUILayout.PropertyField(attrProp, new GUIContent("Attribute"));
            var attr = (AttributeType)attrProp.enumValueIndex;

            bool isDamageReduction = (attr == AttributeType.DamageReduction);

            // DamageReduction：固定百分比+Self，并说明
            if (isDamageReduction)
            {
                if (modProp != null) modProp.enumValueIndex = (int)ModifierType.Percentage;
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(modProp, new GUIContent("Modifier Type (fixed as Percentage)"));
                EditorGUI.EndDisabledGroup();

                if (tgtProp != null) tgtProp.enumValueIndex = (int)TargetType.Self;
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.EnumPopup(new GUIContent("Target (fixed)"), TargetType.Self);
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.HelpBox(
                    "Damage Reduction 按最终承伤结算：实际伤害 × (1 - value)。\n" +
                    "示例：value=0.25 → 25%减伤。建议 Duration 设 -1（随状态在）或具体回合数。",
                    MessageType.Info);
            }
            else
            {
                // 其他属性正常显示 ModifierType
                EditorGUILayout.PropertyField(modProp, new GUIContent("Modifier Type"));
            }

            EditorGUILayout.Space(4);

            // ====== 值编辑（支持 per-level & 单值 + 快捷芯片）======
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Values"))
            {
                bool collapsed;
                if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
                {
                    if (!collapsed)
                    {
                        var levels = elem.FindPropertyRelative("valueExprLevels");
                        PerLevelUI.DrawStringLevels(levels, "Value Expression by Level");

                        // 快速阶梯：等差（百分比/平铺二选一预览）
                        EditorGUILayout.Space(2);
                        EditorGUILayout.BeginVertical("box");
                        EditorGUILayout.LabelField("Quick Fill (arithmetic ramp)", EditorStyles.miniBoldLabel);
                        s_PercentageModePreview = EditorGUILayout.ToggleLeft("Percentage mode (treat numbers as 0.10 = 10%)", s_PercentageModePreview);
                        EditorGUILayout.BeginHorizontal();
                        s_Base = EditorGUILayout.FloatField(new GUIContent("Base"), s_Base);
                        s_Step = EditorGUILayout.FloatField(new GUIContent("Step"), s_Step);
                        EditorGUILayout.EndHorizontal();

                        if (GUILayout.Button("Fill L1~L4 (use numbers as typed)"))
                        {
                            if (levels != null && levels.isArray)
                            {
                                for (int i = 0; i < 4; i++)
                                {
                                    float v = s_Base + s_Step * i;
                                    // 百分比模式下，10 → 0.10 更顺手（避免误填 10.0 = +1000%）
                                    string expr = s_PercentageModePreview ? (v >= 1f ? (v / 100f).ToString("0.###") : v.ToString("0.###"))
                                                                          : v.ToString("0.###");
                                    levels.GetArrayElementAtIndex(i).stringValue = expr;
                                }
                            }
                        }
                        EditorGUILayout.EndVertical();
                    }

                    int curLv = LevelContext.GetSkillLevel(elem.serializedObject);
                    bool showD = FieldVisibilityUI.Has(elem, EffectFieldMask.Duration);
                    bool showP = FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
                    bool showS = FieldVisibilityUI.Has(elem, EffectFieldMask.Stacks);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, curLv, showD, showP, showS);
                }
                else
                {
                    var valProp = elem.FindPropertyRelative("valueExpression");
                    EditorGUILayout.PropertyField(valProp,
                        new GUIContent("Value Expression (e.g. '10', '0.1', 'p', 'atk*0.5')"));

                    // 快捷芯片（根据 ModifierType 分组）
                    var mod = (ModifierType)(modProp != null ? modProp.enumValueIndex : (int)ModifierType.Flat);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Quick:", GUILayout.Width(38));
                    if (mod == ModifierType.Percentage)
                    {
                        s_QuickPct = EditorGUILayout.FloatField(s_QuickPct, GUILayout.Width(50)); // 0.10
                        if (GUILayout.Button("5%", GUILayout.Width(40))) SetExpr(valProp, "0.05");
                        if (GUILayout.Button("10%", GUILayout.Width(45))) SetExpr(valProp, "0.10");
                        if (GUILayout.Button("25%", GUILayout.Width(45))) SetExpr(valProp, "0.25");
                        if (GUILayout.Button("50%", GUILayout.Width(45))) SetExpr(valProp, "0.50");
                        if (GUILayout.Button("p", GUILayout.Width(30))) SetExpr(valProp, "p");  // 直接用职业精通占比
                    }
                    else
                    {
                        s_QuickFlat = EditorGUILayout.FloatField(s_QuickFlat, GUILayout.Width(50)); // 10
                        if (GUILayout.Button("+5", GUILayout.Width(36))) SetExpr(valProp, "5");
                        if (GUILayout.Button("+10", GUILayout.Width(40))) SetExpr(valProp, "10");
                        if (GUILayout.Button("+20", GUILayout.Width(40))) SetExpr(valProp, "20");
                        if (GUILayout.Button("+50", GUILayout.Width(40))) SetExpr(valProp, "50");
                    }
                    if (GUILayout.Button("Clear", GUILayout.Width(50))) SetExpr(valProp, string.Empty);
                    EditorGUILayout.EndHorizontal();

                    // 概率 & 层数
                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));

                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Stacks, "Stacks"))
                        DrawStackCountField(elem);
                }
            }

            // 目标（DR 已固定不再显示）
            if (!isDamageReduction && FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Target"))
                EditorGUILayout.PropertyField(tgtProp, new GUIContent("Target"));

            // 条件
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                var cond = elem.FindPropertyRelative("condition");
                EditorGUILayout.PropertyField(cond, new GUIContent("Trigger Condition"));
                FieldVisibilityUI.DrawConditionFields(elem, cond);
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
            EditorGUILayout.PropertyField(stacksProp, new GUIContent("Stack Count"));
            if (stacksProp.intValue < 1) stacksProp.intValue = 1;
        }

        private void SetExpr(SerializedProperty prop, string v)
        {
            if (prop != null) prop.stringValue = v ?? string.Empty;
        }

        private void DrawLine()
        {
            var r = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.25f));
        }
    }
}
