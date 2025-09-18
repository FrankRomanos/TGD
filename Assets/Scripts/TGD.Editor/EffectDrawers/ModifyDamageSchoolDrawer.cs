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
            var modifyTypeProp = elem.FindPropertyRelative("damageSchoolModifyType");
            if (modifyTypeProp != null)
                EditorGUILayout.PropertyField(modifyTypeProp, new GUIContent("Modify Type"));
            DamageSchoolModifyType modifyType = modifyTypeProp != null
                ? (DamageSchoolModifyType)modifyTypeProp.enumValueIndex
                : DamageSchoolModifyType.Damage;

            DrawFilter(elem);

            switch (modifyType)
            {
                case DamageSchoolModifyType.Damage:
                    DrawDamageModification(elem);
                    break;
                case DamageSchoolModifyType.DamageSchoolType:
                    DrawSchoolConversion(elem);
                    break;
            }

            DrawProbability(elem);
            DrawCondition(elem);
            DrawSummary(elem, modifyType);
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

        private static void DrawFilter(SerializedProperty elem)
        {
            var filterToggle = elem.FindPropertyRelative("damageSchoolFilterEnabled");
            var filterProp = elem.FindPropertyRelative("damageSchoolFilter");

            if (filterToggle == null || filterProp == null)
                return;

            EditorGUILayout.PropertyField(filterToggle, new GUIContent("Use Damage School Filter"));
            if (filterToggle.boolValue)
            {
                EditorGUILayout.PropertyField(filterProp, new GUIContent("Only Affect School"));
                EditorGUILayout.HelpBox("When enabled, only actions matching the selected school are modified.", MessageType.Info);
            }
        }

        private static void DrawDamageModification(SerializedProperty elem)
        {
            var schoolProp = elem.FindPropertyRelative("damageSchool");
            if (schoolProp != null)
                EditorGUILayout.PropertyField(schoolProp, new GUIContent("Affected School"));

            var opProp = elem.FindPropertyRelative("skillModifyOperation");
            if (opProp != null)
                EditorGUILayout.PropertyField(opProp, new GUIContent("Operation"));
        
            var modifierProp = elem.FindPropertyRelative("modifierType");
            if (modifierProp != null)
     
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
        private static void DrawSchoolConversion(SerializedProperty elem)
        {
            var schoolProp = elem.FindPropertyRelative("damageSchool");
            if (schoolProp != null)
                EditorGUILayout.PropertyField(schoolProp, new GUIContent("New Damage School"));
            else
                EditorGUILayout.HelpBox("'damageSchool' property not found on effect.", MessageType.Warning);
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

        private static void DrawSummary(SerializedProperty elem, DamageSchoolModifyType modifyType)
        {
            var skillProp = elem.FindPropertyRelative("targetSkillID");
            string skillId = skillProp != null ? skillProp.stringValue : string.Empty;
            string skillLabel = string.IsNullOrWhiteSpace(skillId) ? "owning skill" : $"skill '{skillId}'";

            var filterToggle = elem.FindPropertyRelative("damageSchoolFilterEnabled");
            bool hasFilter = filterToggle != null && filterToggle.boolValue;
            string filterLabel = string.Empty;
            if (hasFilter)
            {
                var filterProp = elem.FindPropertyRelative("damageSchoolFilter");
                if (filterProp != null)
                    filterLabel = $" filtered by {((DamageSchool)filterProp.enumValueIndex)}";
            }

            switch (modifyType)
            {
                case DamageSchoolModifyType.Damage:
                    var schoolProp = elem.FindPropertyRelative("damageSchool");
                    string school = schoolProp != null ? ((DamageSchool)schoolProp.enumValueIndex).ToString() : "Unknown";

                    var opProp = elem.FindPropertyRelative("skillModifyOperation");
                    string operation = opProp != null ? ((SkillModifyOperation)opProp.enumValueIndex).ToString() : SkillModifyOperation.Add.ToString();

                    var modifierProp = elem.FindPropertyRelative("modifierType");
                    string modifier = modifierProp != null ? ((ModifierType)modifierProp.enumValueIndex).ToString() : ModifierType.Percentage.ToString();

                    var valueProp = FieldVisibilityUI.GetProp(elem, "valueExpression", "value");
                    string valueText = DescribeValue(valueProp);

                    string summary = $"Adjust {skillLabel}{filterLabel} {school} damage ({modifier}, {operation})";
                    if (!string.IsNullOrWhiteSpace(valueText))
                        summary += $" by '{valueText}'";
                    summary += ".";

                    EditorGUILayout.HelpBox(summary, MessageType.None);
                    break;
                case DamageSchoolModifyType.DamageSchoolType:
                    var newSchoolProp = elem.FindPropertyRelative("damageSchool");
                    string newSchool = newSchoolProp != null ? ((DamageSchool)newSchoolProp.enumValueIndex).ToString() : "Unknown";
                    EditorGUILayout.HelpBox($"Convert {skillLabel}{filterLabel} damage into {newSchool}.", MessageType.None);
                    break;
            }
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