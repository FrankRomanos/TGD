using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class AttributeModifierDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Attribute Modifier", EditorStyles.boldLabel);

            var attrProp = elem.FindPropertyRelative("attributeType");
            var modProp = elem.FindPropertyRelative("modifierType");
            var tgtProp = elem.FindPropertyRelative("target");

            // Attribute
            EditorGUILayout.PropertyField(attrProp, new GUIContent("Attribute"));
            AttributeType attr = (AttributeType)attrProp.enumValueIndex;

            // ====== NEW: DamageReduction ר UI Լ ======
            bool isDamageReduction = (attr == AttributeType.DamageReduction);

            if (isDamageReduction)
            {
                // ModifierType 固定为 Percentage
                if (modProp != null) modProp.enumValueIndex = (int)ModifierType.Percentage;
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(modProp, new GUIContent("Modifier Type (fixed as Percentage)"));
                EditorGUI.EndDisabledGroup();

                // Target 固定为 Self
                if (tgtProp != null) tgtProp.enumValueIndex = (int)TargetType.Self;
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.EnumPopup(new GUIContent("Target (fixed)"), TargetType.Self);
                EditorGUI.EndDisabledGroup();

                // 说明
                EditorGUILayout.HelpBox(
                    "Damage Reduction: 最终承伤按 (1 - value) 结算；例如 0.25 = 25%减伤。\n" +
                    "建议把 Duration 设为 -1（随状态存续），或具体回合数。",
                    MessageType.Info);
            }
            else
            {
                // 非 DamageReduction 正常显示 ModifierType
                EditorGUILayout.PropertyField(modProp, new GUIContent("Modifier Type"));
            }


            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Values"))
            {
                bool collapsed;
                if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
                {
                    // per-level ON
                    if (!collapsed)
                    {
                        PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("valueExprLevels"),
                            "Value Expression by Level");


                        if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                            PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("probabilityLvls"),
                                "Probability by Level (%)");

                        if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Stacks, "Stacks"))
                            PerLevelUI.DrawIntLevels(elem.FindPropertyRelative("stackCountLevels"),
                                "Stacks by Level");
                    }

                    int curLv = LevelContext.GetSkillLevel(elem.serializedObject);
                    bool showD = FieldVisibilityUI.Has(elem, EffectFieldMask.Duration);
                    bool showP = FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, curLv, showD, showP,
         FieldVisibilityUI.Has(elem, EffectFieldMask.Stacks));
                }
                else
                {
                    // per-level OFF → 单值
                    EditorGUILayout.PropertyField(
                        elem.FindPropertyRelative("valueExpression"),
                        new GUIContent("Value Expression (e.g. '10', 'p', 'atk*0.5')")
                    );

                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"),
                         new GUIContent("Probability (%)"));

                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Stacks, "Stacks"))
                        DrawStackCountField(elem);
                }
            }

            // Target：DamageReduction 已经固定为 Self，不再显示开关；其他属性照常可选
            if (!isDamageReduction && FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Target"))
            {
                EditorGUILayout.PropertyField(tgtProp, new GUIContent("Target"));
            }

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
            if (stacksProp.intValue < 1)
                stacksProp.intValue = 1;
        }
    }
}
