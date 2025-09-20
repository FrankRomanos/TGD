using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class AuraDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Aura", EditorStyles.boldLabel);

            var radiusProp = elem.FindPropertyRelative("auraRadius");
            if (radiusProp != null)
                EditorGUILayout.PropertyField(radiusProp, new GUIContent("Radius"));

            var categoryProp = elem.FindPropertyRelative("auraCategories");
            if (categoryProp != null)
                EditorGUILayout.PropertyField(categoryProp, new GUIContent("Effect Category"));

            var affectedProp = elem.FindPropertyRelative("auraTarget");
            if (affectedProp != null)
                EditorGUILayout.PropertyField(affectedProp, new GUIContent("Affected Targets"));

            var immuneProp = elem.FindPropertyRelative("auraAffectsImmune");
            if (immuneProp != null)
                EditorGUILayout.PropertyField(immuneProp, new GUIContent("Affect Immune Targets"));

            var durationProp = elem.FindPropertyRelative("auraDuration");
            if (durationProp != null)
                EditorGUILayout.PropertyField(durationProp, new GUIContent("Aura Duration (turns)"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Source Target"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new GUIContent("Source Target"));

            var conditionProp = elem.FindPropertyRelative("condition");
            EditorGUILayout.PropertyField(conditionProp, new GUIContent("Trigger Condition"));
            FieldVisibilityUI.DrawConditionFields(elem, conditionProp);
        }
    }
}