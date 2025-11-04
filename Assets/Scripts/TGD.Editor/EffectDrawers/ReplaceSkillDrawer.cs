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
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("inheritReplacedCooldown"),
    new UnityEngine.GUIContent("Inherit Cooldown State"));
            EditorGUILayout.HelpBox("Enable to keep the existing cooldown when swapping skills. Disable to refresh and start a new cooldown cycle for the replacement skill.",
                MessageType.Info);
        }
    }
}

