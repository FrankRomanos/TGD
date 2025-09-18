using TGD.Data;
using UnityEditor;
using UnityEngine;

namespace TGD.Editor
{
    public class RandomOutcomeDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Random Outcome", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(elem.FindPropertyRelative("randomRollCount"), new GUIContent("Roll Count"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("randomAllowDuplicates"), new GUIContent("Allow Duplicates"));

            var outcomes = elem.FindPropertyRelative("randomOutcomes");
            if (outcomes != null)
            {
                EditorGUILayout.PropertyField(outcomes, new GUIContent("Outcome Entries"), includeChildren: true);
                if (outcomes.arraySize == 0)
                    EditorGUILayout.HelpBox("Add entries to configure dice faces / random options.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("'randomOutcomes' property not found on effect.", MessageType.Warning);
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                var cond = elem.FindPropertyRelative("condition");
                EditorGUILayout.PropertyField(cond, new GUIContent("Trigger Condition"));
                FieldVisibilityUI.DrawConditionFields(elem, cond);
            }
        }
    }
}
