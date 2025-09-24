using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class DamageDrawer : IEffectDrawer
    {
        // 记住上次的阶梯配置，方便多次填写
        private static float s_BaseAtkMul = 1.0f;
        private static float s_StepPerLevel = 0.1f;

        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Damage", EditorStyles.boldLabel);
            DrawLine();

            // 学派/暴击
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.School, "Damage School"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("damageSchool"), new GUIContent("School"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Crit, "Critical"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("canCrit"), new GUIContent("Can Crit"));

            EditorGUILayout.Space(4);

            // 数值区
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Values"))
            {
                bool collapsed;
                if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
                {
                    if (!collapsed)
                    {
                        // 按等级字符串数组
                        var levelsProp = elem.FindPropertyRelative("valueExprLevels");
                        PerLevelUI.DrawStringLevels(levelsProp, "Value Expression by Level");

                        // 快捷：基于 atk 的阶梯填充
                        EditorGUILayout.Space(2);
                        EditorGUILayout.BeginVertical("box");
                        EditorGUILayout.LabelField("Quick Fill (atk ramp)", EditorStyles.miniBoldLabel);
                        EditorGUILayout.BeginHorizontal();
                        s_BaseAtkMul = EditorGUILayout.FloatField(new GUIContent("Base (L1) ×atk"), s_BaseAtkMul);
                        s_StepPerLevel = EditorGUILayout.FloatField(new GUIContent("Step per Level"), s_StepPerLevel);
                        EditorGUILayout.EndHorizontal();

                        if (GUILayout.Button("Fill L1~L4 as atk * (Base + Step * levelIndex)"))
                        {
                            if (levelsProp != null && levelsProp.isArray)
                            {
                                for (int i = 0; i < 4; i++)
                                {
                                    float m = s_BaseAtkMul + s_StepPerLevel * i;
                                    levelsProp.GetArrayElementAtIndex(i).stringValue = $"atk*{m:0.###}";
                                }
                            }
                        }
                        EditorGUILayout.EndVertical();
                    }

                    // 预览
                    int curLv = LevelContext.GetSkillLevel(elem.serializedObject);
                    bool showP = FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, curLv, showDuration: false, showProb: showP);
                }
                else
                {
                    // 单值表达式
                    var valProp = elem.FindPropertyRelative("valueExpression");
                    EditorGUILayout.PropertyField(valProp,
                        new GUIContent("Value Expression (e.g. 'atk*1.2')"));

                    // 快捷：一键写入 atk×N
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Quick:", GUILayout.Width(38));
                    if (GUILayout.Button("atk×1.0", GUILayout.Width(70))) SetExpr(valProp, "atk*1.0");
                    if (GUILayout.Button("×1.2", GUILayout.Width(50))) SetExpr(valProp, "atk*1.2");
                    if (GUILayout.Button("×1.5", GUILayout.Width(50))) SetExpr(valProp, "atk*1.5");
                    if (GUILayout.Button("Clear", GUILayout.Width(50))) SetExpr(valProp, string.Empty);
                    EditorGUILayout.EndHorizontal();

                    // 概率（可选）
                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));
                }
            }

            EditorGUILayout.Space(6);

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
        }

        private void SetExpr(SerializedProperty prop, string value)
        {
            if (prop != null) prop.stringValue = value;
        }

        private void DrawLine()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.25f));
        }
    }
}
