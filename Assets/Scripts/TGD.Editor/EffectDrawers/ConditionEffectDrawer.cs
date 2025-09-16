using UnityEditor;
using UnityEngine;

namespace TGD.Editor
{
    public class ConditionalEffectDrawer : DefaultEffectDrawer
    {
        public override void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Conditional Effect", EditorStyles.boldLabel);

            // 条件
            EditorGUILayout.LabelField("Condition", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("resourceType"), new UnityEngine.GUIContent("Resource Type"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("compareOp"), new UnityEngine.GUIContent("Compare Operator"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("compareValue"), new UnityEngine.GUIContent("Compare Value"));

            // 子效果列表
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("On Success Effects", EditorStyles.boldLabel);

            var onSuccess = elem.FindPropertyRelative("onSuccess");
            if (onSuccess != null)
            {
                for (int i = 0; i < onSuccess.arraySize; i++)
                {
                    var sub = onSuccess.GetArrayElementAtIndex(i);
                    EditorGUILayout.BeginVertical("box");

                    var subTypeProp = sub.FindPropertyRelative("effectType");
                    EditorGUILayout.PropertyField(subTypeProp, new UnityEngine.GUIContent("Effect Type"));

                    var drawer = EffectDrawerRegistry.Get((TGD.Data.EffectType)subTypeProp.enumValueIndex);
                    drawer.Draw(sub);

                    if (GUILayout.Button("Remove Success Effect"))
                        onSuccess.DeleteArrayElementAtIndex(i);

                    EditorGUILayout.EndVertical();
                }

                if (GUILayout.Button("Add Success Effect"))
                    onSuccess.InsertArrayElementAtIndex(onSuccess.arraySize);
            }
        }
    }
}
