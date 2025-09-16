using UnityEditor;
using UnityEngine;

namespace TGD.Editor
{
    public class ModifyActionDamageDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Modify Action Damage", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(elem.FindPropertyRelative("targetActionType"), new GUIContent("Action Type"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("modifierType"), new GUIContent("Modifier Type"));

            if (FieldVisibilityUI.Toggle(elem, TGD.Data.EffectFieldMask.PerLevel, "Per-Level Values"))
            {
                bool collapsed;
                if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
                {
                    if (!collapsed)
                        PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("valueExprLevels"), "Value Expression by Level");

                    int curLv = LevelContext.GetSkillLevel(elem.serializedObject);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, curLv, showDuration: false, showProb: false);
                }
                else
                {
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("valueExpression"),
                        new GUIContent("Value Expression (e.g. 'p', 'atk*0.5')"));
                }
            }

            if (FieldVisibilityUI.Toggle(elem, TGD.Data.EffectFieldMask.Condition, "Trigger Condition"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("condition"), new GUIContent("Trigger Condition"));
        }
    }
}

