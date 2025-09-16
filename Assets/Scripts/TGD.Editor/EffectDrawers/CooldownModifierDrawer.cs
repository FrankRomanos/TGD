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

            var skillIdProp = elem.FindPropertyRelative("targetSkillID");
            if (skillIdProp != null)
            {
                EditorGUILayout.PropertyField(skillIdProp, new GUIContent("Target Skill ID"));
                if (string.IsNullOrWhiteSpace(skillIdProp.stringValue))
                {
                    EditorGUILayout.HelpBox("Leave empty to affect the owning skill.", MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("'targetSkillID' property not found on effect.", MessageType.Warning);
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

            if ((EffectCondition)condProp.enumValueIndex == EffectCondition.OnNextSkillSpendResource)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("conditionResourceType"), new GUIContent("Cond. Resource"));
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("conditionMinAmount"), new GUIContent("Min Spend"));
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("consumeStatusOnTrigger"), new GUIContent("Consume Status On Trigger"));
            }
        }
    }
}