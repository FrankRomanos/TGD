using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>
    /// Drawer for EffectType.CooldownModifier effects.
    /// Allows skills to modify the cooldown of other skills at runtime.
    /// </summary>
    public class CooldownModifierDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Cooldown Modifier", EditorStyles.boldLabel);

            var scopeProp = elem.FindPropertyRelative("cooldownTargetScope");
            if (scopeProp != null)
            {
                EditorGUILayout.PropertyField(scopeProp, new GUIContent("Target Scope"));
                EditorGUILayout.HelpBox(
                    "Self: affects the casting skill only. All: affects every skill. ExceptRed: affects every skill except Red (ultimate) skills.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("'cooldownTargetScope' property not found on effect.", MessageType.Warning);
            }

            var changeProp = elem.FindPropertyRelative("cooldownChangeSeconds");
            if (changeProp != null)
            {
                EditorGUILayout.PropertyField(changeProp, new GUIContent("Cooldown Change (seconds)"));
                EditorGUILayout.HelpBox(
                    "Negative values reduce cooldown. Positive values extend it. The interpreter rounds values to the closest turn step (minimum ¡À1 round).",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("'cooldownChangeSeconds' property not found on effect.", MessageType.Warning);
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                DrawCondition(elem);
            }
        }

        private void DrawCondition(SerializedProperty elem)
        {
            var condProp = elem.FindPropertyRelative("condition");
            EditorGUILayout.PropertyField(condProp, new GUIContent("Trigger Condition"));

            FieldVisibilityUI.DrawConditionFields(elem, condProp);
        }
    }
}