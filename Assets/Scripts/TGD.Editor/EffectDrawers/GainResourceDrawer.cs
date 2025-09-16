using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class GainResourceDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Gain Resource", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("resourceType"), new GUIContent("Resource Type"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Values"))
            {
                bool collapsed;
                if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
                {
                    if (!collapsed)
                        PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("valueExprLevels"), "Value by Level (formula)");

                    int curLv = LevelContext.GetSkillLevel(elem.serializedObject);
                    bool showP = FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, curLv, showDuration: false, showProb: showP);
                }
                else
                {
                    var valExpr = FieldVisibilityUI.GetProp(elem, "valueExpression", "value");
                    if (valExpr != null)
                        EditorGUILayout.PropertyField(valExpr, new GUIContent("Value (formula, e.g. '1', 'p', 'atk*0.1')"));
                    else
                        EditorGUILayout.HelpBox("Missing 'valueExpression' (or legacy 'value') field.", MessageType.Warning);

                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));
                }
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Target"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new GUIContent("Target"));


            var cond = elem.FindPropertyRelative("condition");
            EditorGUILayout.PropertyField(cond, new GUIContent("Trigger Condition"));
            // 仅当选择 OnNextSkillSpendResource 时展示附加参数
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





