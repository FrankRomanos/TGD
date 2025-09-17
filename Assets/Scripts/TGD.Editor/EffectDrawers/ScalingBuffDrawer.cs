using UnityEditor;

namespace TGD.Editor
{
    public class ScalingBuffDrawer : DefaultEffectDrawer
    {
        public override void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Scaling Buff (per resource)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("resourceType"), new UnityEngine.GUIContent("Resource Type"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("scalingValuePerResource"), new UnityEngine.GUIContent("Value/Res (formula)"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("maxStacks"), new UnityEngine.GUIContent("Max Stacks (0 = )"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("scalingAttribute"), new UnityEngine.GUIContent("Attribute Affected"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new UnityEngine.GUIContent("Target"));
            var conditionProp = elem.FindPropertyRelative("condition");
            EditorGUILayout.PropertyField(conditionProp, new UnityEngine.GUIContent("Trigger Condition"));
            FieldVisibilityUI.DrawConditionFields(elem, conditionProp);
        }
    }
}

