using System;
using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>
    /// ͳһ�ġ���ʾ/���ؿ��ء���Ⱦ��λ�����д��
    /// </summary>
    public static class FieldVisibilityUI
    {
        /// ��ѡһ���ɼ��Կ��أ����أ���ǰ�Ƿ���ʾ������
        public static bool Toggle(SerializedProperty elem, EffectFieldMask flag, string label)
        {
            var maskProp = elem.FindPropertyRelative("visibleFields");
            var mask = (EffectFieldMask)maskProp.intValue;

            bool on = (mask & flag) != 0;
            bool newOn = EditorGUILayout.ToggleLeft($"Show {label}", on);
            if (newOn != on)
            {
                if (newOn) mask |= flag; else mask &= ~flag;
                maskProp.intValue = (int)mask;
            }
            return newOn;
        }

        /// ��ȡ�Ƿ����ĳ flag
        public static bool Has(SerializedProperty elem, EffectFieldMask flag)
        {
            var maskProp = elem.FindPropertyRelative("visibleFields");
            var mask = (EffectFieldMask)maskProp.intValue;
            return (mask & flag) != 0;
        }

        /// ���鳤��У��
        public static void EnsureSize(SerializedProperty arr, int n)
        {
            while (arr.arraySize < n) arr.InsertArrayElementAtIndex(arr.arraySize);
            while (arr.arraySize > n) arr.DeleteArrayElementAtIndex(arr.arraySize - 1);
        }

        /// ���ݾ��ֶ����Ļ�ȡ��������������ѡ������
        public static SerializedProperty GetProp(SerializedProperty elem, string mainName, params string[] altNames)
        {
            var p = elem.FindPropertyRelative(mainName);
            if (p != null) return p;
            foreach (var n in altNames)
            {
                p = elem.FindPropertyRelative(n);
                if (p != null) return p;
            }
            return null;
        }

        public static void DrawConditionFields(SerializedProperty elem, SerializedProperty conditionProp = null)
        {
            if (elem == null)
                return;

            var condProp = conditionProp ?? elem.FindPropertyRelative("condition");
            if (condProp == null)
                return;

            var condition = (EffectCondition)condProp.enumValueIndex;
            switch (condition)
            {
                case EffectCondition.AfterSkillUse:
                    var skillProp = elem.FindPropertyRelative("conditionSkillUseID");
                    if (skillProp != null)
                    {
                        EditorGUILayout.PropertyField(skillProp, new GUIContent("Skill Use ID"));
                        if (string.IsNullOrWhiteSpace(skillProp.stringValue) ||
                            string.Equals(skillProp.stringValue, "any", StringComparison.OrdinalIgnoreCase))
                        {
                            EditorGUILayout.HelpBox("Leave empty or enter 'any' to react to any skill usage.", MessageType.Info);
                        }
                    }
                    break;
                case EffectCondition.SkillStateActive:
                    var stateProp = elem.FindPropertyRelative("conditionSkillStateID");
                    if (stateProp != null)
                    {
                        EditorGUILayout.PropertyField(stateProp, new GUIContent("State Skill ID"));
                        if (string.IsNullOrWhiteSpace(stateProp.stringValue))
                        {
                            EditorGUILayout.HelpBox("Leave empty to react to any active state.", MessageType.Info);
                        }
                    }
                    break;
                case EffectCondition.OnNextSkillSpendResource:
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("conditionResourceType"),
                        new GUIContent("Cond. Resource"));
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("conditionMinAmount"),
                        new GUIContent("Min Spend"));
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("consumeStatusOnTrigger"),
                        new GUIContent("Consume Status On Trigger"));
                    break;
            }
        }
    }
}
