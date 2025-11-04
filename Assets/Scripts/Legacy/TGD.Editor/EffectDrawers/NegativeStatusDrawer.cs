using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>
    /// Drawer for configuring EffectType.NegativeStatus entries in the inspector.
    /// </summary>
    public class NegativeStatusDrawer : IEffectDrawer
    {
        private const int BaseTurnSeconds = 6;

        private static readonly GUIContent SecondsLabel = new("Seconds");
        private static readonly GUIContent StunHeader = new("Stun", "Each full base turn (6s) will skip a turn. Remaining time reduces next turn's base duration.");
        private static readonly GUIContent EntangleHeader = new("Entangle");
        private static readonly GUIContent SlowHeader = new("Slow");
        private static readonly GUIContent SluggishHeader = new("Sluggish", "Reduces the target's base turn duration. Values above the base turn (6s) are clamped and will kill the target.");
        private static readonly GUIContent MovementReductionLabel = new("Movement Reduction");
        private static readonly GUIContent DisableNonForcedLabel = new("Disable Non-Forced Movement");

        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Negative Status", EditorStyles.boldLabel);

            DrawNegativeStatusList(elem);

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Duration, "Duration"))
            {
                var durationProp = elem.FindPropertyRelative("duration");
                if (durationProp != null)
                    EditorGUILayout.PropertyField(durationProp, new GUIContent("Duration (turns)"));
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
            {
                var probabilityProp = elem.FindPropertyRelative("probability");
                if (probabilityProp != null)
                    EditorGUILayout.PropertyField(probabilityProp, new GUIContent("Probability (%)"));
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Target"))
            {
                var targetProp = elem.FindPropertyRelative("target");
                if (targetProp != null)
                    EditorGUILayout.PropertyField(targetProp, new GUIContent("Target"));
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                var conditionProp = elem.FindPropertyRelative("condition");
                if (conditionProp != null)
                {
                    EditorGUILayout.PropertyField(conditionProp, new GUIContent("Trigger Condition"));
                    FieldVisibilityUI.DrawConditionFields(elem, conditionProp);
                }
            }
        }

        private static void DrawNegativeStatusList(SerializedProperty effectProp)
        {
            var listProp = effectProp.FindPropertyRelative("negativeStatuses");
            if (listProp == null)
            {
                EditorGUILayout.HelpBox("'negativeStatuses' property not found on effect.", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Configured Statuses", EditorStyles.boldLabel);

            if (listProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Add entries to configure stun, entangle, slow or sluggish debuffs.", MessageType.Info);
            }

            for (int i = 0; i < listProp.arraySize; i++)
            {
                var entry = listProp.GetArrayElementAtIndex(i);
                if (entry == null)
                    continue;

                EditorGUILayout.BeginVertical("box");
                bool removed = DrawNegativeStatusEntry(entry, listProp, i);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2f);

                if (removed)
                    break;
            }

            if (GUILayout.Button("Add Negative Status"))
            {
                int newIndex = listProp.arraySize;
                listProp.InsertArrayElementAtIndex(newIndex);
                ResetEntry(listProp.GetArrayElementAtIndex(newIndex));
            }
        }

        private static bool DrawNegativeStatusEntry(SerializedProperty entry, SerializedProperty listProp, int index)
        {
            bool removed = false;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Status #{index + 1}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Remove", GUILayout.Width(70f)))
            {
                NestedEffectListDrawer.RemoveArrayElement(listProp, index);
                removed = true;
            }
            EditorGUILayout.EndHorizontal();

            if (removed)
                return true;

            var typeProp = entry.FindPropertyRelative("statusType");
            if (typeProp != null)
                EditorGUILayout.PropertyField(typeProp, new GUIContent("Type"));

            NegativeStatusType type = typeProp != null
                ? (NegativeStatusType)typeProp.enumValueIndex
                : NegativeStatusType.Stun;

            switch (type)
            {
                case NegativeStatusType.Stun:
                    DrawStunFields(entry);
                    break;
                case NegativeStatusType.Entangle:
                    DrawEntangleFields(entry);
                    break;
                case NegativeStatusType.Slow:
                    DrawSlowFields(entry);
                    break;
                case NegativeStatusType.Sluggish:
                    DrawSluggishFields(entry);
                    break;
                default:
                    EditorGUILayout.HelpBox($"Unsupported status type: {type}", MessageType.Warning);
                    break;
            }

            return false;
        }

        private static void DrawStunFields(SerializedProperty entry)
        {
            EditorGUILayout.LabelField(StunHeader, EditorStyles.boldLabel);

            var secondsProp = entry.FindPropertyRelative("seconds");
            if (secondsProp != null)
            {
                EditorGUILayout.PropertyField(secondsProp, SecondsLabel);
                if (secondsProp.floatValue < 0f)
                    secondsProp.floatValue = 0f;

                float skippedTurns = BaseTurnSeconds > 0
                    ? Mathf.Floor(secondsProp.floatValue / BaseTurnSeconds)
                    : 0f;
                float remainder = secondsProp.floatValue - skippedTurns * BaseTurnSeconds;
                if (remainder > 0f)
                {
                    EditorGUILayout.HelpBox($"Skips {skippedTurns:0} turn(s) and reduces next base time by {remainder:0.##}s.", MessageType.None);
                }
                else if (secondsProp.floatValue > 0f)
                {
                    EditorGUILayout.HelpBox($"Skips {skippedTurns:0} turn(s).", MessageType.None);
                }
            }
        }

        private static void DrawEntangleFields(SerializedProperty entry)
        {
            EditorGUILayout.LabelField(EntangleHeader, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Movement is forced to 0 while the status is active.", MessageType.Info);

            var disableProp = entry.FindPropertyRelative("disableNonForcedMovement");
            if (disableProp != null)
                EditorGUILayout.PropertyField(disableProp, DisableNonForcedLabel);
        }

        private static void DrawSlowFields(SerializedProperty entry)
        {
            EditorGUILayout.LabelField(SlowHeader, EditorStyles.boldLabel);

            var movementProp = entry.FindPropertyRelative("movementReduction");
            if (movementProp != null)
            {
                EditorGUILayout.PropertyField(movementProp, MovementReductionLabel);
                if (movementProp.intValue < 0)
                    movementProp.intValue = 0;
                EditorGUILayout.HelpBox("Movement cannot be reduced below 1.", MessageType.None);
            }
        }

        private static void DrawSluggishFields(SerializedProperty entry)
        {
            EditorGUILayout.LabelField(SluggishHeader, EditorStyles.boldLabel);

            var secondsProp = entry.FindPropertyRelative("seconds");
            if (secondsProp != null)
            {
                EditorGUILayout.PropertyField(secondsProp, SecondsLabel);
                if (secondsProp.floatValue < 0f)
                    secondsProp.floatValue = 0f;

                if (secondsProp.floatValue > BaseTurnSeconds)
                {
                    EditorGUILayout.HelpBox($"Values above {BaseTurnSeconds}s are clamped and will kill the target.", MessageType.Warning);
                }
                else if (secondsProp.floatValue >= BaseTurnSeconds)
                {
                    EditorGUILayout.HelpBox("Base turn time reduced to zero; the target dies when the effect resolves.", MessageType.Warning);
                }
                else if (secondsProp.floatValue > 0f)
                {
                    EditorGUILayout.HelpBox($"Base turn time reduced by {secondsProp.floatValue:0.##}s.", MessageType.None);
                }
            }
        }

        private static void ResetEntry(SerializedProperty entry)
        {
            if (entry == null)
                return;

            var typeProp = entry.FindPropertyRelative("statusType");
            if (typeProp != null)
                typeProp.enumValueIndex = (int)NegativeStatusType.Stun;

            var secondsProp = entry.FindPropertyRelative("seconds");
            if (secondsProp != null)
                secondsProp.floatValue = 0f;

            var movementProp = entry.FindPropertyRelative("movementReduction");
            if (movementProp != null)
                movementProp.intValue = 0;

            var disableProp = entry.FindPropertyRelative("disableNonForcedMovement");
            if (disableProp != null)
                disableProp.boolValue = true;
        }
    }
}