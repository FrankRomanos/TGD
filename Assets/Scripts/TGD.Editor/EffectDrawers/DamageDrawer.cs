using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class DamageDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Damage", EditorStyles.boldLabel);

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.School, "Damage School"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("damageSchool"), new GUIContent("School"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Crit, "Critical"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("canCrit"), new GUIContent("Can Crit"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Values"))
            {
                bool collapsed;
                if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
                {
                    if (!collapsed)
                        PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("valueExprLevels"), "Value Expression by Level");

                    int curLv = LevelContext.GetSkillLevel(elem.serializedObject);
                    bool showP = FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, curLv, showDuration: false, showProb: showP);
                }
                else
                {
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("valueExpression"),
                        new GUIContent("Value Expression (e.g. 'atk*1.2')"));

                    if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));
                }
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Target"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new GUIContent("Target"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                var conditionProp = elem.FindPropertyRelative("condition");
                EditorGUILayout.PropertyField(conditionProp, new GUIContent("Trigger Condition"));
                FieldVisibilityUI.DrawConditionFields(elem, conditionProp);
            }
        }
    }
}
