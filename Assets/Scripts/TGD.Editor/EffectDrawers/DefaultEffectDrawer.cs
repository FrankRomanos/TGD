using UnityEditor;

namespace TGD.Editor
{
    /// <summary>ͨ�ö��׻��ƣ��ȸ��������ֶΣ�����հ�</summary>
    public class DefaultEffectDrawer : IEffectDrawer
    {
        public virtual void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Effect Details", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("value"), new UnityEngine.GUIContent("Value"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("duration"), new UnityEngine.GUIContent("Duration (Turn)"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new UnityEngine.GUIContent("Target"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("statusSkillID"), new UnityEngine.GUIContent("Status Skill ID"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new UnityEngine.GUIContent("Probability (%)"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("condition"), new UnityEngine.GUIContent("Trigger Condition"));
        }
    }
}

