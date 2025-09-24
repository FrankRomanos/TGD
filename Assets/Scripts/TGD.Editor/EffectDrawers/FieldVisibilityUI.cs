using System;
using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>
    /// 统一的“显示/隐藏开关”渲染与位掩码读写。
    /// </summary>
    public static class FieldVisibilityUI
    {
        /// 勾选一个可见性开关；返回：当前是否显示该区块
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

        /// 读取是否包含某 flag
        public static bool Has(SerializedProperty elem, EffectFieldMask flag)
        {
            var maskProp = elem.FindPropertyRelative("visibleFields");
            var mask = (EffectFieldMask)maskProp.intValue;
            return (mask & flag) != 0;
        }

        /// 数组长度校正
        public static void EnsureSize(SerializedProperty arr, int n)
        {
            while (arr.arraySize < n) arr.InsertArrayElementAtIndex(arr.arraySize);
            while (arr.arraySize > n) arr.DeleteArrayElementAtIndex(arr.arraySize - 1);
        }

        /// 兼容旧字段名的获取（优先主名，次选别名）
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

                    var checkStacksProp = elem.FindPropertyRelative("conditionSkillStateCheckStacks");
                    var compareProp = elem.FindPropertyRelative("conditionSkillStateStackCompare");
                    var stacksProp = elem.FindPropertyRelative("conditionSkillStateStacks");
                    if (checkStacksProp != null && compareProp != null && stacksProp != null)
                    {
                        EditorGUILayout.PropertyField(checkStacksProp, new GUIContent("Check Stacks"));
                        if (checkStacksProp.boolValue)
                        {
                            CompareOp[] options = { CompareOp.Greater, CompareOp.Less, CompareOp.Equal };
                            string[] labels = { ">", "<", "==" };
                            int current = System.Array.IndexOf(options, (CompareOp)compareProp.enumValueIndex);
                            if (current < 0) current = 0;
                            int selected = EditorGUILayout.Popup("Stack Comparison", current, labels);
                            compareProp.enumValueIndex = (int)options[selected];
                            EditorGUILayout.PropertyField(stacksProp, new GUIContent("Required Stacks"));
                        }
                    }
                    break;
                case EffectCondition.OnDotHotActive:
                    var targetProp = elem.FindPropertyRelative("conditionDotTarget");
                    if (targetProp != null)
                        EditorGUILayout.PropertyField(targetProp, new GUIContent("Effect Target"));

                    var dotList = elem.FindPropertyRelative("conditionDotSkillIDs");
                    if (dotList != null)
                    {
                        EditorGUILayout.PropertyField(dotList, new GUIContent("DoT Skill IDs"), includeChildren: true);
                        if (dotList.isArray && dotList.arraySize == 0)
                            EditorGUILayout.HelpBox("Leave empty to react to any DoT/HoT skill.", MessageType.Info);
                    }

                    var stackProp = elem.FindPropertyRelative("conditionDotUseStacks");
                    if (stackProp != null)
                        EditorGUILayout.PropertyField(stackProp, new GUIContent("Use Stack Count"));
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

            if (condition == EffectCondition.AfterAttack ||
                condition == EffectCondition.OnPerformAttack ||
                   condition == EffectCondition.OnPerformHeal ||
                condition == EffectCondition.LinkCancelled)
            {
                var targetProp = elem.FindPropertyRelative("conditionTarget");
                if (targetProp != null)
                    EditorGUILayout.PropertyField(targetProp, new GUIContent("Condition Target"));
            }
        }
    }
}
