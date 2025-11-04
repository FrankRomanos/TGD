using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class AuraDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            // 头部 + 摘要
            var summary = BuildAuraSummary(elem);
            EditorUIUtil.Header("Aura", summary, EditorUIUtil.ColorForEffectType(EffectType.Aura));

            // —— 形状 & 范围 —— //
            EditorUIUtil.BoxScope(() =>
            {
                EditorGUILayout.LabelField("Shape & Range", EditorStyles.boldLabel);
                var rangeModeProp = elem.FindPropertyRelative("auraRangeMode");
                if (rangeModeProp != null)
                    EditorGUILayout.PropertyField(rangeModeProp, new GUIContent("Range Mode"));

                var mode = rangeModeProp != null ? (AuraRangeMode)rangeModeProp.enumValueIndex : AuraRangeMode.Within;
                switch (mode)
                {
                    case AuraRangeMode.Between:
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("auraMinRadius"), new GUIContent("Min Radius"));
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("auraMaxRadius"), new GUIContent("Max Radius"));
                        break;
                    case AuraRangeMode.Exact:
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("auraRadius"), new GUIContent("Exact Radius"));
                        break;
                    default: // Within
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("auraRadius"), new GUIContent("Radius (≤)"));
                        break;
                }
            });

            EditorUIUtil.Separator();

            // —— 目标 & 类别 —— //
            EditorUIUtil.BoxScope(() =>
            {
                EditorGUILayout.LabelField("Targets & Category", EditorStyles.boldLabel);
                var categoryProp = elem.FindPropertyRelative("auraCategories");
                if (categoryProp != null)
                    EditorGUILayout.PropertyField(categoryProp, new GUIContent("Effect Category"));

                var affectedProp = elem.FindPropertyRelative("auraTarget");
                if (affectedProp != null)
                    EditorGUILayout.PropertyField(affectedProp, new GUIContent("Affected Targets"));

                var immuneProp = elem.FindPropertyRelative("auraAffectsImmune");
                if (immuneProp != null)
                    EditorGUILayout.PropertyField(immuneProp, new GUIContent("Affect Immune Targets"));

                if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Source Target"))
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new GUIContent("Source Target"));
            });

            EditorUIUtil.Separator();

            // —— 时序（持续/心跳） —— //
            EditorUIUtil.BoxScope(() =>
            {
                EditorGUILayout.LabelField("Timing", EditorStyles.boldLabel);
                var durationProp = elem.FindPropertyRelative("auraDuration");
                if (durationProp != null)
                    EditorGUILayout.PropertyField(durationProp, new GUIContent("Aura Duration (turns)"));

                var heartProp = elem.FindPropertyRelative("auraHeartSeconds");
                if (heartProp != null)
                    EditorGUILayout.PropertyField(heartProp, new GUIContent("Heartbeat (seconds)"));
            });

            EditorUIUtil.Separator();

            // —— 进入/离开条件 —— //
            EditorUIUtil.BoxScope(() =>
            {
                EditorGUILayout.LabelField("Enter / Exit Conditions", EditorStyles.boldLabel);
                var onEnterProp = elem.FindPropertyRelative("auraOnEnter");
                if (onEnterProp != null)
                    EditorGUILayout.PropertyField(onEnterProp, new GUIContent("On Enter"));

                var onExitProp = elem.FindPropertyRelative("auraOnExit");
                if (onExitProp != null)
                    EditorGUILayout.PropertyField(onExitProp, new GUIContent("On Exit"));

                if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Trigger Probability"))
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));

                var conditionProp = elem.FindPropertyRelative("condition");
                EditorGUILayout.PropertyField(conditionProp, new GUIContent("Trigger Condition"));
                FieldVisibilityUI.DrawConditionFields(elem, conditionProp);
            });

            EditorUIUtil.Separator();

            // —— 附加效果（嵌套） —— //
            var extra = elem.FindPropertyRelative("auraAdditionalEffects");
            if (extra != null)
            {
                NestedEffectListDrawer.DrawEffectsList(extra, elem.depth + 1, "Additional Effects");
            }
        }

        private static string BuildAuraSummary(SerializedProperty elem)
        {
            var modeProp = elem.FindPropertyRelative("auraRangeMode");
            var mode = modeProp != null ? (AuraRangeMode)modeProp.enumValueIndex : AuraRangeMode.Within;

            switch (mode)
            {
                case AuraRangeMode.Between:
                    float min = elem.FindPropertyRelative("auraMinRadius")?.floatValue ?? 0f;
                    float max = elem.FindPropertyRelative("auraMaxRadius")?.floatValue ?? 0f;
                    return $"Between {min}-{max}";
                case AuraRangeMode.Exact:
                    float exact = elem.FindPropertyRelative("auraRadius")?.floatValue ?? 0f;
                    return $"Exact {exact}";
                default:
                    float r = elem.FindPropertyRelative("auraRadius")?.floatValue ?? 0f;
                    return $"Within {r}";
            }
        }
    }
}
