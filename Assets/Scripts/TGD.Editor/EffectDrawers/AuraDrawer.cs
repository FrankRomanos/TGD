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

            var rangeModeProp = elem.FindPropertyRelative("auraRangeMode");
            AuraRangeMode rangeMode = rangeModeProp != null
                ? (AuraRangeMode)rangeModeProp.enumValueIndex
                : AuraRangeMode.Within;
            if (rangeModeProp != null)
                EditorGUILayout.PropertyField(rangeModeProp, new GUIContent("Range Mode"));

            switch (rangeMode)
            {
                case AuraRangeMode.Between:
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("auraMinRadius"), new GUIContent("Min Radius"));
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("auraMaxRadius"), new GUIContent("Max Radius"));
                    break;
                default:
                    var radiusProp = elem.FindPropertyRelative("auraRadius");
                    if (radiusProp != null)
                        EditorGUILayout.PropertyField(radiusProp, new GUIContent("Radius"));
                    break;
            }

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
            var heartProp = elem.FindPropertyRelative("auraHeartSeconds");
            if (heartProp != null)
                EditorGUILayout.PropertyField(heartProp, new GUIContent("Heartbeat (seconds)"));

            var onEnterProp = elem.FindPropertyRelative("auraOnEnter");
            if (onEnterProp != null)
                EditorGUILayout.PropertyField(onEnterProp, new GUIContent("On Enter"));

            var onExitProp = elem.FindPropertyRelative("auraOnExit");
            if (onExitProp != null)
                EditorGUILayout.PropertyField(onExitProp, new GUIContent("On Exit"));


            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Source Target"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new GUIContent("Source Target"));

            var conditionProp = elem.FindPropertyRelative("condition");
            EditorGUILayout.PropertyField(conditionProp, new GUIContent("Trigger Condition"));
            FieldVisibilityUI.DrawConditionFields(elem, conditionProp);

            var extra = elem.FindPropertyRelative("auraAdditionalEffects");
            if (extra != null)
            {
                EditorGUILayout.Space();
                NestedEffectListDrawer.DrawEffectsList(extra, elem.depth + 1, "Additional Effects");
            }
        }
    }
}