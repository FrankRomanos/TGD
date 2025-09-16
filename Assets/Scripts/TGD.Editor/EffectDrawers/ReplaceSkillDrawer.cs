using UnityEditor;

namespace TGD.Editor
{
    public class ReplaceSkillDrawer : DefaultEffectDrawer
    {
        public override void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Replace Skill", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("targetSkillID"), new UnityEngine.GUIContent("Original Skill ID"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("replaceSkillID"), new UnityEngine.GUIContent("New Skill ID"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("duration"), new UnityEngine.GUIContent("Duration (turns)"));
        }
    }
}

