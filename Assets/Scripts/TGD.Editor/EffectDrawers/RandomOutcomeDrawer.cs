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
                if (outcomes.arraySize == 0)
                    EditorGUILayout.HelpBox("Add entries to configure dice faces / random options.", MessageType.Info);

                for (int i = 0; i < outcomes.arraySize; i++)
                {
                    var entry = outcomes.GetArrayElementAtIndex(i);
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("label"), new GUIContent("Label"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("description"), new GUIContent("Description"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("weight"), new GUIContent("Weight"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("probabilityMode"), new GUIContent("Probability Mode"));

                    var effects = entry.FindPropertyRelative("effects");
                    // Delegate to the shared drawer so nested outcome effects remain editable and cycle-safe.
                    NestedEffectListDrawer.DrawEffectsList(effects, entry.depth + 1, "Outcome Effects");

                    if (GUILayout.Button("Remove Outcome"))
                    {
                        NestedEffectListDrawer.RemoveArrayElement(outcomes, i);
                        EditorGUILayout.EndVertical();
                        break;
                    }
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(3f);
                }

                if (GUILayout.Button("Add Outcome"))
                {
                    int newIndex = outcomes.arraySize;
                    outcomes.InsertArrayElementAtIndex(newIndex);
                    ResetOutcomeEntry(outcomes.GetArrayElementAtIndex(newIndex));
                }
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
        private void ResetOutcomeEntry(SerializedProperty entry)
        {
            if (entry == null)
                return;
            // 先获取属性，再判断是否为null，不为null才赋值
            var labelProp = entry.FindPropertyRelative("label");
            if (labelProp != null)
            {
                labelProp.stringValue = string.Empty;
            }

            var descProp = entry.FindPropertyRelative("description");
            if (descProp != null)
            {
                descProp.stringValue = string.Empty;
            }
            var weightProp = entry.FindPropertyRelative("weight");
            if (weightProp != null)
                weightProp.intValue = 1;

            var modeProp = entry.FindPropertyRelative("probabilityMode");
            if (modeProp != null)
                modeProp.enumValueIndex = (int)ProbabilityModifierMode.None;

            var effects = entry.FindPropertyRelative("effects");
            if (effects != null && effects.isArray)
            {
                NestedEffectListDrawer.ClearArray(effects);
            }
        }
    }
}
