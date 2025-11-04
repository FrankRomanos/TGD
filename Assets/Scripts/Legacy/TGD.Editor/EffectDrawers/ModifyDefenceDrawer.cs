using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class ModifyDefenceDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Modify Defence", EditorStyles.boldLabel);

            var modeProp = elem.FindPropertyRelative("defenceMode");
            if (modeProp != null)
                EditorGUILayout.PropertyField(modeProp, new GUIContent("Mode"));

            DefenceModificationMode mode = modeProp != null
                ? (DefenceModificationMode)modeProp.enumValueIndex
                : DefenceModificationMode.Shield;

            bool probabilityHandled = false;
            switch (mode)
            {
                case DefenceModificationMode.Shield:
                    probabilityHandled = DrawShieldBlock(elem);
                    break;
                case DefenceModificationMode.DamageRedirect:
                    DrawRedirectBlock(elem);
                    break;
                case DefenceModificationMode.Reflect:
                    DrawReflectBlock(elem);
                    break;
                case DefenceModificationMode.Immunity:
                    DrawImmunityBlock(elem);
                    break;
            }

            if (!probabilityHandled && FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Duration, "Duration"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("duration"), new GUIContent("Duration (turns)"));

            if (mode == DefenceModificationMode.Shield && FieldVisibilityUI.Toggle(elem, EffectFieldMask.Stacks, "Stacks"))
                DrawStackField(elem);

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Target"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new GUIContent("Target"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                var conditionProp = elem.FindPropertyRelative("condition");
                EditorGUILayout.PropertyField(conditionProp, new GUIContent("Trigger Condition"));
                FieldVisibilityUI.DrawConditionFields(elem, conditionProp);
            }
        }

        private bool DrawShieldBlock(SerializedProperty elem)
        {
            bool probabilityHandled = false;

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Shield Values"))
            {
                bool collapsed;
                if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
                {
                    if (!collapsed)
                    {
                        PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("valueExprLevels"),
                            "Shield Value by Level");

                        if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                        {
                            PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("probabilityLvls"),
                                "Probability by Level (%)");
                            probabilityHandled = true;
                        }

                        if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Stacks, "Stacks"))
                            PerLevelUI.DrawIntLevels(elem.FindPropertyRelative("stackCountLevels"),
                                "Stacks by Level");
                    }

                    int currentLevel = LevelContext.GetSkillLevel(elem.serializedObject);
                    bool showDuration = FieldVisibilityUI.Has(elem, EffectFieldMask.Duration);
                    bool showProbability = FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
                    bool showStacks = FieldVisibilityUI.Has(elem, EffectFieldMask.Stacks);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, currentLevel, showDuration, showProbability, showStacks);
                    probabilityHandled |= FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
                }
            }
            else
            {
                var valueProp = FieldVisibilityUI.GetProp(elem, "valueExpression", "value");
                if (valueProp != null)
                    EditorGUILayout.PropertyField(valueProp, new GUIContent("Shield Value / Expression"));
            }

            var maxExpressionProp = elem.FindPropertyRelative("defenceShieldMaxExpression");
            if (maxExpressionProp != null)
                EditorGUILayout.PropertyField(maxExpressionProp, new GUIContent("Max Shield (Expression)"));

            var maxValueProp = elem.FindPropertyRelative("defenceShieldMaxValue");
            if (maxValueProp != null)
                EditorGUILayout.PropertyField(maxValueProp, new GUIContent("Max Shield (Fallback)"));

            var perSchoolProp = elem.FindPropertyRelative("defenceShieldUsePerSchool");
            if (perSchoolProp != null)
            {
                EditorGUILayout.PropertyField(perSchoolProp, new GUIContent("Split By Damage School"));
                if (perSchoolProp.boolValue)
                {
                    var listProp = elem.FindPropertyRelative("defenceShieldBreakdown");
                    if (listProp != null)
                        EditorGUILayout.PropertyField(listProp, new GUIContent("Damage School Values"), includeChildren: true);
                }
            }

            return probabilityHandled;
        }

        private void DrawRedirectBlock(SerializedProperty elem)
        {
            var ratioExprProp = elem.FindPropertyRelative("defenceRedirectExpression");
            if (ratioExprProp != null)
                EditorGUILayout.PropertyField(ratioExprProp, new GUIContent("Redirect Ratio (Expression)"));

            var ratioValueProp = elem.FindPropertyRelative("defenceRedirectRatio");
            if (ratioValueProp != null)
                EditorGUILayout.PropertyField(ratioValueProp, new GUIContent("Redirect Ratio (Fallback)"));

            var targetProp = elem.FindPropertyRelative("defenceRedirectTarget");
            if (targetProp != null)
                EditorGUILayout.PropertyField(targetProp, new GUIContent("Redirect Target (Condition)"));
        }

        private void DrawReflectBlock(SerializedProperty elem)
        {
            var useIncomingProp = elem.FindPropertyRelative("defenceReflectUseIncomingDamage");
            if (useIncomingProp != null)
                EditorGUILayout.PropertyField(useIncomingProp, new GUIContent("Use Incoming Damage Ratio"));

            if (useIncomingProp == null || useIncomingProp.boolValue)
            {
                var ratioExprProp = elem.FindPropertyRelative("defenceReflectRatioExpression");
                if (ratioExprProp != null)
                    EditorGUILayout.PropertyField(ratioExprProp, new GUIContent("Reflect Ratio (Expression)"));

                var ratioValueProp = elem.FindPropertyRelative("defenceReflectRatio");
                if (ratioValueProp != null)
                    EditorGUILayout.PropertyField(ratioValueProp, new GUIContent("Reflect Ratio (Fallback)"));
            }

            var flatExprProp = elem.FindPropertyRelative("defenceReflectFlatExpression");
            if (flatExprProp != null)
                EditorGUILayout.PropertyField(flatExprProp, new GUIContent("Flat Damage (Expression)"));

            var flatValueProp = elem.FindPropertyRelative("defenceReflectFlatDamage");
            if (flatValueProp != null)
                EditorGUILayout.PropertyField(flatValueProp, new GUIContent("Flat Damage (Fallback)"));

            var schoolProp = elem.FindPropertyRelative("defenceReflectDamageSchool");
            if (schoolProp != null)
                EditorGUILayout.PropertyField(schoolProp, new GUIContent("Damage School"));
        }

        private void DrawImmunityBlock(SerializedProperty elem)
        {
            var scopeProp = elem.FindPropertyRelative("immunityScope");
            if (scopeProp != null)
                EditorGUILayout.PropertyField(scopeProp, new GUIContent("Immunity Scope"));

            var listProp = elem.FindPropertyRelative("defenceImmuneSkillIDs");
            if (listProp != null)
                EditorGUILayout.PropertyField(listProp, new GUIContent("Immune Skill IDs"), includeChildren: true);

            EditorGUILayout.HelpBox(
             "Set Immunity Scope to 'OnlySkill' to block effects from the listed skills. Leave the list empty to apply the selected scope globally.",
             MessageType.Info);
        }

        private void DrawStackField(SerializedProperty elem)
        {
            var stackProp = elem.FindPropertyRelative("stackCount");
            if (stackProp == null)
            {
                EditorGUILayout.HelpBox("'stackCount' property not found on effect.", MessageType.Warning);
                return;
            }

            EditorGUILayout.PropertyField(stackProp, new GUIContent("Stack Count"));
            if (stackProp.intValue < 1)
                stackProp.intValue = 1;
        }
    }
}
