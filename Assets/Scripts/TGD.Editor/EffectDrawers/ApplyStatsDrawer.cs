using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class ApplyStatusDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Apply Status (Buff/Debuff)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("statusSkillID"), new GUIContent("Status Skill ID"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Values"))
            {
                bool collapsed;
                if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
                {
                    if (!collapsed)
                    {
                        if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Duration, "Duration"))
                            PerLevelUI.DrawIntLevels(elem.FindPropertyRelative("durationLevels"), "Duration by Level (turns)");

                        if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                            PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("probabilityLvls"), "Probability by Level (%)");
                    }

                    int curLv = LevelContext.GetSkillLevel(elem.serializedObject);
                    bool showD = FieldVisibilityUI.Has(elem, EffectFieldMask.Duration);
                    bool showP = FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, curLv, showD, showP);
                }
                else
                {

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

