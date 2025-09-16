using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TGD.Editor
{
    /// <summary>���õ� L1~L4 ���� + �۵� + Ԥ������</summary>
    public static class PerLevelUI
    {
        private static readonly Dictionary<string, bool> Collapsed = new();

        public static void EnsureSize(SerializedProperty arr, int n)
        {
            while (arr.arraySize < n) arr.InsertArrayElementAtIndex(arr.arraySize);
            while (arr.arraySize > n) arr.DeleteArrayElementAtIndex(arr.arraySize - 1);
        }

        public static void DrawStringLevels(SerializedProperty arr, string label)
        {
            EnsureSize(arr, 4);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            for (int i = 0; i < 4; i++)
                EditorGUILayout.PropertyField(arr.GetArrayElementAtIndex(i), new GUIContent($"L{i + 1}"));
            EditorGUI.indentLevel--;
        }

        public static void DrawIntLevels(SerializedProperty arr, string label)
        {
            EnsureSize(arr, 4);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            for (int i = 0; i < 4; i++)
                EditorGUILayout.PropertyField(arr.GetArrayElementAtIndex(i), new GUIContent($"L{i + 1}"));
            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// ����Use Per-Level Values�� + �۵���ť�����أ�perLevel �Ƿ�����out collapsed���Ƿ��۵���
        /// </summary>
        public static bool BeginPerLevelBlock(SerializedProperty elem, out bool collapsed, string useLabel = "Use Per-Level Values")
        {
            var perLevel = elem.FindPropertyRelative("perLevel");
            var key = elem.propertyPath + "_collapsed";

            Collapsed.TryGetValue(key, out collapsed);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(perLevel, new GUIContent(useLabel));
            GUILayout.FlexibleSpace();
            var newCollapsed = GUILayout.Toggle(collapsed, collapsed ? "Show Edit Fields" : "Hide Edit Fields", EditorStyles.miniButton);
            if (newCollapsed != collapsed)
            {
                collapsed = newCollapsed;
                Collapsed[key] = collapsed;
            }
            EditorGUILayout.EndHorizontal();

            return perLevel.boolValue;
        }

        /// <summary>ֻ��Ԥ����չʾ��ǰ���ܵȼ����õ���ֵ��</summary>
        public static void DrawPreviewForCurrentLevel(
            SerializedProperty elem, int level, bool showDuration, bool showProb)
        {
            var vals = elem.FindPropertyRelative("valueExprLevels");
            var durs = elem.FindPropertyRelative("durationLevels");
            var probs = elem.FindPropertyRelative("probabilityLvls");

            EnsureSize(vals, 4);
            EnsureSize(durs, 4);
            EnsureSize(probs, 4);

            int idx = Mathf.Clamp(level - 1, 0, 3);

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox($"Preview (Use Skill Level {level})", MessageType.Info);
            EditorGUI.indentLevel++;

            var v = vals.GetArrayElementAtIndex(idx).stringValue;
            EditorGUILayout.LabelField($"Value @L{level}", string.IsNullOrEmpty(v) ? "(empty)" : v);

            if (showDuration)
            {
                var d = durs.GetArrayElementAtIndex(idx).intValue;
                EditorGUILayout.LabelField($"Duration @L{level}", d.ToString());
            }

            if (showProb)
            {
                var p = probs.GetArrayElementAtIndex(idx).stringValue;
                EditorGUILayout.LabelField($"Probability @L{level}", string.IsNullOrEmpty(p) ? "(empty)" : p);
            }

            EditorGUI.indentLevel--;
        }
    }
}
