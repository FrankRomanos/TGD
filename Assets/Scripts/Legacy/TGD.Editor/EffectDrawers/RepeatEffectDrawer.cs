using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class RepeatEffectDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Repeat Nested Effects", EditorStyles.boldLabel);

            var sourceProp = elem.FindPropertyRelative("repeatCountSource");
            EditorGUILayout.PropertyField(sourceProp, new GUIContent("Count Source"));
            RepeatCountSource source = sourceProp != null
                ? (RepeatCountSource)sourceProp.enumValueIndex
                : RepeatCountSource.Fixed;

            EditorGUILayout.PropertyField(elem.FindPropertyRelative("repeatCount"), new GUIContent("Base Count"));

            if (source == RepeatCountSource.Expression)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("repeatCountExpression"), new GUIContent("Count Expression"));
                EditorGUILayout.HelpBox("使用表达式计算重复次数，留空则使用 Base Count。", MessageType.Info);
            }
            else if (source == RepeatCountSource.ResourceValue || source == RepeatCountSource.ResourceSpent)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("repeatResourceType"), new GUIContent("Resource Type"));
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("repeatConsumeResource"), new GUIContent("Consume Resource"));
            }
            else
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("repeatConsumeResource"), new GUIContent("Consume Resource"));
            }

            EditorGUILayout.PropertyField(elem.FindPropertyRelative("repeatMaxCount"), new GUIContent("Max Count (0 = unlimited)"));

            var effectsProp = elem.FindPropertyRelative("repeatEffects");
            if (effectsProp != null)
            {
                // Ensure nested repeat payloads use the shared UI helper to avoid recursion depth issues.
                NestedEffectListDrawer.DrawEffectsList(effectsProp, elem.depth + 1, "Repeated Effects");
            }
            else
                EditorGUILayout.HelpBox("'repeatEffects' property not found on effect.", MessageType.Warning);

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                var cond = elem.FindPropertyRelative("condition");
                EditorGUILayout.PropertyField(cond, new GUIContent("Trigger Condition"));
                FieldVisibilityUI.DrawConditionFields(elem, cond);
            }
        }
    }
}