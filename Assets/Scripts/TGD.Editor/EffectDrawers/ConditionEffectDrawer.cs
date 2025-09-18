using UnityEditor;
using UnityEngine;

namespace TGD.Editor
{
    public class ConditionalEffectDrawer : DefaultEffectDrawer
    {
        public override void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Conditional Effect", EditorStyles.boldLabel);

            // ����
            EditorGUILayout.LabelField("Condition", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("resourceType"), new UnityEngine.GUIContent("Resource Type"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("compareOp"), new UnityEngine.GUIContent("Compare Operator"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("compareValue"), new UnityEngine.GUIContent("Compare Value"));

            // ��Ч���б�
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("On Success Effects", EditorStyles.boldLabel);

            var onSuccess = elem.FindPropertyRelative("onSuccess");
            if (onSuccess != null)
            {
                NestedEffectListDrawer.DrawEffectsList(onSuccess, elem.depth + 1, "On Success Effects");
            }
            else
            {
                EditorGUILayout.HelpBox("'onSuccess' property not found on effect.", MessageType.Warning);
            }
        }
    }
}
