using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>
    /// Drawer for EffectType.CooldownModifier
    /// 依赖字段：
    /// - targetSkillID (string)
    /// - cooldownChangeSeconds (int, 秒；负数=减冷却，正数=加冷却)
    /// - condition (EffectCondition) [可见性开关]
    /// </summary>
    public class CooldownModifierDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Cooldown Modifier", EditorStyles.boldLabel);

            // 目标技能ID（不走可见性开关，始终显示，避免额外的 EffectFieldMask 枚举扩展）
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("targetSkillID"),
                new GUIContent("Target Skill ID"));

            // 冷却变化（秒）
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("cooldownChangeSeconds"),
                new GUIContent("Cooldown Change (seconds)"));

            EditorGUILayout.HelpBox("负数 = 减少冷却；正数 = 增加冷却。单位：秒。", MessageType.Info);

            // 触发条件（可隐藏/显示，沿用你现有的 Condition 开关）
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Show Trigger Condition"))
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("condition"),
                    new GUIContent("Trigger Condition"));
            }
        }
    }
}
