using TGD.Data;
using UnityEditor;
using UnityEngine;

namespace TGD.Editor
{
    public class RandomOutcomeDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            // 顶部标题 + 小徽标
            EditorUIUtil.Header("Random Outcome", "DICE", EditorUIUtil.ColorForEffectType(EffectType.RandomOutcome));

            // —— General 设置 —— //
            EditorUIUtil.BoxScope(() =>
            {
                EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("randomRollCount"), new GUIContent("Roll Count"));
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("randomAllowDuplicates"), new GUIContent("Allow Duplicates"));
            });

            EditorUIUtil.Separator();

            // —— Outcomes 列表 —— //
            var outcomes = elem.FindPropertyRelative("randomOutcomes");
            if (outcomes == null)
            {
                EditorGUILayout.HelpBox("'randomOutcomes' property not found on effect.", MessageType.Warning);
                return;
            }

            EditorUIUtil.BoxScope(() =>
            {
                EditorUIUtil.Header("Outcomes", $"{outcomes.arraySize} option(s)", new Color(1f, 0.8f, 0.3f, 0.25f));

                // 工具条
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("＋ Add Outcome", GUILayout.Height(20)))
                {
                    int newIndex = outcomes.arraySize;
                    outcomes.InsertArrayElementAtIndex(newIndex);
                    ResetOutcomeEntry(outcomes.GetArrayElementAtIndex(newIndex));
                    GUIUtility.ExitGUI();
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                EditorUIUtil.Separator();

                for (int i = 0; i < outcomes.arraySize; i++)
                {
                    var entry = outcomes.GetArrayElementAtIndex(i);
                    if (entry == null) continue;

                    string label = entry.FindPropertyRelative("label")?.stringValue;
                    if (string.IsNullOrEmpty(label)) label = $"Outcome {i + 1}";

                    // 每个 Outcome 折叠块
                    var foldKey = $"{outcomes.propertyPath}#{i}";
                    bool open = EditorUIUtil.Foldout(foldKey, label, defaultState: true);

                    // 行内操作：上移/下移/删除
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUI.enabled = i > 0;
                    if (GUILayout.Button("▲", GUILayout.Width(24))) { outcomes.MoveArrayElement(i, i - 1); GUIUtility.ExitGUI(); }
                    GUI.enabled = i < outcomes.arraySize - 1;
                    if (GUILayout.Button("▼", GUILayout.Width(24))) { outcomes.MoveArrayElement(i, i + 1); GUIUtility.ExitGUI(); }
                    GUI.enabled = true;
                    if (GUILayout.Button("X", GUILayout.Width(24))) { NestedEffectListDrawer.RemoveArrayElement(outcomes, i); GUIUtility.ExitGUI(); }
                    EditorGUILayout.EndHorizontal();

                    if (!open) { EditorUIUtil.Separator(0.5f, 2f); continue; }

                    // 内容体
                    EditorUIUtil.BoxScope(() =>
                    {
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("label"), new GUIContent("Label"));
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("description"), new GUIContent("Description"));
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("weight"), new GUIContent("Weight"));
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("probabilityMode"), new GUIContent("Probability Mode"));

                        EditorUIUtil.Separator();

                        // 子效果列表
                        var effects = entry.FindPropertyRelative("effects");
                        NestedEffectListDrawer.DrawEffectsList(effects, entry.depth + 1, "Outcome Effects");
                    });
                    EditorUIUtil.Separator();
                }
            });

            // —— 条件（可见性开关沿用你项目里的 FieldVisibilityUI） —— //
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                var cond = elem.FindPropertyRelative("condition");
                EditorGUILayout.PropertyField(cond, new GUIContent("Trigger Condition"));
                FieldVisibilityUI.DrawConditionFields(elem, cond);
            }
        }

        private void ResetOutcomeEntry(SerializedProperty entry)
        {
            if (entry == null) return;

            var labelProp = entry.FindPropertyRelative("label");
            if (labelProp != null) labelProp.stringValue = string.Empty;

            var descProp = entry.FindPropertyRelative("description");
            if (descProp != null) descProp.stringValue = string.Empty;

            var weightProp = entry.FindPropertyRelative("weight");
            if (weightProp != null) weightProp.intValue = 1;

            var modeProp = entry.FindPropertyRelative("probabilityMode");
            if (modeProp != null) modeProp.enumValueIndex = (int)ProbabilityModifierMode.None;

            var effects = entry.FindPropertyRelative("effects");
            if (effects != null && effects.isArray)
                NestedEffectListDrawer.ClearArray(effects);
        }
    }
}
