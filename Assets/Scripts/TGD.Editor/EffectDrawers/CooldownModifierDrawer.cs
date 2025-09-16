using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>
    /// Drawer for EffectType.CooldownModifier
    /// �����ֶΣ�
    /// - targetSkillID (string)
    /// - cooldownChangeSeconds (int, �룻����=����ȴ������=����ȴ)
    /// - condition (EffectCondition) [�ɼ��Կ���]
    /// </summary>
    public class CooldownModifierDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Cooldown Modifier", EditorStyles.boldLabel);

            // Ŀ�꼼��ID�����߿ɼ��Կ��أ�ʼ����ʾ���������� EffectFieldMask ö����չ��
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("targetSkillID"),
                new GUIContent("Target Skill ID"));

            // ��ȴ�仯���룩
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("cooldownChangeSeconds"),
                new GUIContent("Cooldown Change (seconds)"));

            EditorGUILayout.HelpBox("���� = ������ȴ������ = ������ȴ����λ���롣", MessageType.Info);

            // ����������������/��ʾ�����������е� Condition ���أ�
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Show Trigger Condition"))
            {
                var cond = elem.FindPropertyRelative("condition");
                EditorGUILayout.PropertyField(cond, new GUIContent("Trigger Condition"));

                // ������Ϊ OnNextSkillSpendResource ʱ����ʾ�����������GainResource�߼�һ�£�
                if ((EffectCondition)cond.enumValueIndex == EffectCondition.OnNextSkillSpendResource)
                {
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("conditionResourceType"),
                        new GUIContent("Cond. Resource"));
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("conditionMinAmount"),
                        new GUIContent("Min Spend"));
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("consumeStatusOnTrigger"),
                        new GUIContent("Consume Status On Trigger"));
                }
            }
        }
    }
}
