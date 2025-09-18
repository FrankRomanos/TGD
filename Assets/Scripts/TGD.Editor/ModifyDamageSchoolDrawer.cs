using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class ModifyDamageSchoolDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Modify Damage School", EditorStyles.boldLabel);

            DrawTargetSkill(elem);
            DrawSchool(elem);
            DrawOperation(elem);
            DrawModifierType(elem);
            DrawValue(elem);
            DrawProbability(elem);
            DrawCondition(elem);
            DrawSummary(elem);
        }

        private static void DrawTargetSkill(SerializedProperty elem)
        {
            var skillProp = elem.FindPropertyRelative("targetSkillID");
            if (skillProp == null)
                return;

            EditorGUILayout.PropertyField(skillProp, new GUIContent("Modify Skill ID"));
            if (string.IsNullOrWhiteSpace(skillProp.stringValue))
            {
                EditorGUILayout.HelpBox("Leave empty to affect the owning skill.", MessageType.Info);
            }
        }

        private static void DrawSchool(SerializedProperty elem)
        {
            var schoolProp = elem.FindPropertyRelative("damageSchool");
            if (schoolProp != null)
                EditorGUILayout.PropertyField(schoolProp, new GUIContent("Damage School"));
        }

        private static void DrawOperation(SerializedProperty elem)
        {
            var opProp = elem.FindPropertyRelative("skillModifyOperation");
            if (opProp != null)
                EditorGUILayout.PropertyField(opProp, new GUIContent("Operation"));
        }

        private static void DrawModifierType(SerializedProperty elem)
        {
            var modifierProp = elem.FindPropertyRelative("modifierType");
            if (modifierProp != null)
                EditorGUILayout.PropertyField(modifierProp, new GUIContent("Modifier Type"));
        }

        private static void DrawValue(SerializedProperty elem)
        {
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Values"))
            {
                bool collapsed;
                if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
                {
                    if (!collapsed)
                    {
                        PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("valueExprLevels"), "Modifier by Level");
                    }

                    int currentLevel = LevelContext.GetSkillLevel(elem.serializedObject);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, currentLevel, showDuration: false, showProb: false);
                    return;
                }
            }

            var valueProp = FieldVisibilityUI.GetProp(elem, "valueExpression", "value");
            if (valueProp != null)
            {
                EditorGUILayout.PropertyField(valueProp, new GUIContent("Modifier Value"));
            }
            else
            {
                EditorGUILayout.HelpBox("Missing valueExpression/value field on EffectDefinition.", MessageType.Warning);
            }
        }

        private static void DrawProbability(SerializedProperty elem)
        {
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
            {
                var probProp = elem.FindPropertyRelative("probability");
                if (probProp != null)
                    EditorGUILayout.PropertyField(probProp, new GUIContent("Probability (%)"));
            }
        }

        private static void DrawCondition(SerializedProperty elem)
        {
            if (!FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
                return;

            var condProp = elem.FindPropertyRelative("condition");
            EditorGUILayout.PropertyField(condProp, new GUIContent("Trigger Condition"));
            FieldVisibilityUI.DrawConditionFields(elem, condProp);
        }

        private static void DrawSummary(SerializedProperty elem)
        {
            var skillProp = elem.FindPropertyRelative("targetSkillID");
            string skillId = skillProp != null ? skillProp.stringValue : string.Empty;
            string skillLabel = string.IsNullOrWhiteSpace(skillId) ? "owning skill" : $"skill '{skillId}'";

            var schoolProp = elem.FindPropertyRelative("damageSchool");
            string school = schoolProp != null ? ((DamageSchool)schoolProp.enumValueIndex).ToString() : "Unknown";

            var opProp = elem.FindPropertyRelative("skillModifyOperation");
            string operation = opProp != null ? ((SkillModifyOperation)opProp.enumValueIndex).ToString() : SkillModifyOperation.Add.ToString();

            var modifierProp = elem.FindPropertyRelative("modifierType");
            string modifier = modifierProp != null ? ((ModifierType)modifierProp.enumValueIndex).ToString() : ModifierType.Percentage.ToString();

            var valueProp = FieldVisibilityUI.GetProp(elem, "valueExpression", "value");
            string valueText = DescribeValue(valueProp);

            string summary = $"Adjust {skillLabel} {school} damage ({modifier}, {operation})";
            if (!string.IsNullOrWhiteSpace(valueText))
                summary += $" by '{valueText}'";
            summary += ".";

            EditorGUILayout.HelpBox(summary, MessageType.None);
        }

        private static string DescribeValue(SerializedProperty valueProp)
        {
            if (valueProp == null)
                return string.Empty;

            switch (valueProp.propertyType)
            {
                case SerializedPropertyType.String:
                    return valueProp.stringValue;
                case SerializedPropertyType.Float:
                    return valueProp.floatValue.ToString("0.###");
                case SerializedPropertyType.Integer:
                    return valueProp.intValue.ToString();
                default:
                    return string.Empty;
            }
        }
    }
}