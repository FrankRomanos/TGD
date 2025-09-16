// Assets/Scripts/TGD.Editor/EffectDrawers/AttributeModifierDrawer.cs
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

            // ====== NEW: DamageReduction ר�� UI Լ�� ======
            bool isDamageReduction = (attr == AttributeType.DamageReduction);

            if (isDamageReduction)
            {
                // ModifierType �̶�Ϊ Percentage
                if (modProp != null) modProp.enumValueIndex = (int)ModifierType.Percentage;
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(modProp, new GUIContent("Modifier Type (fixed as Percentage)"));
                EditorGUI.EndDisabledGroup();

                // Target �̶�Ϊ Self
                if (tgtProp != null) tgtProp.enumValueIndex = (int)TargetType.Self;
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.EnumPopup(new GUIContent("Target (fixed)"), TargetType.Self);
                EditorGUI.EndDisabledGroup();

                // ˵��
                EditorGUILayout.HelpBox(
                    "Damage Reduction: ���ճ��˰� (1 - value) ���㣻���� 0.25 = 25%���ˡ�\n" +
                    "����� Duration ��Ϊ -1����״̬�������������غ�����",
                    MessageType.Info);
            }
            else
            {
                // �� DamageReduction ������ʾ ModifierType
                EditorGUILayout.PropertyField(modProp, new GUIContent("Modifier Type"));
            }

            // ====== Per-Level ֵ & Ԥ�� ======
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Per-Level Values"))
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
                    }

                    int curLv = LevelContext.GetSkillLevel(elem.serializedObject);
                    bool showD = FieldVisibilityUI.Has(elem, EffectFieldMask.Duration);
                    bool showP = FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, curLv, showD, showP);
                }
                else
                {
                    // per-level OFF �� ��ֵ
                    EditorGUILayout.PropertyField(
                        elem.FindPropertyRelative("valueExpression"),
                        new GUIContent("Value Expression (e.g. '10', 'p', 'atk*0.5')")
                    );



                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));
                }
            }

            // Target��DamageReduction �Ѿ��̶�Ϊ Self��������ʾ���أ����������ճ���ѡ
            if (!isDamageReduction && FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Target"))
            {
                EditorGUILayout.PropertyField(tgtProp, new GUIContent("Target"));
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                var cond = elem.FindPropertyRelative("condition");
                EditorGUILayout.PropertyField(cond, new GUIContent("Trigger Condition"));

                // ������Ϊ OnNextSkillSpendResource ʱ����ʾ�����������GainResource�߼�һ�£�
                if ((EffectCondition)cond.enumValueIndex == EffectCondition.OnNextSkillSpendResource)
                {
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("conditionResourceType"),
                        new GUIContent("Cond. Resource"));
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("conditionMinAmount"),
                        new GUIContent("Min Spend"));
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("consumeStatusOnTrigger"),
                        new GUIContent("Consume Status On Trigger"));
                }
            }
        }
    }
}
