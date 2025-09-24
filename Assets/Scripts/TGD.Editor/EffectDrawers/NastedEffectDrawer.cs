using System;
using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>
    /// 嵌套效果列表：带折叠项标题、上移/下移/删除、展开/折叠全部，避免“挤在一起”
    /// </summary>
    internal static class NestedEffectListDrawer
    {
        private const int MaxNestedDepth = 8;

        public static void DrawEffectsList(SerializedProperty listProp, int depth, string header)
        {
            if (listProp == null)
            {
                EditorGUILayout.HelpBox("Nested effect list property not found.", MessageType.Warning);
                return;
            }
            if (!listProp.isArray)
            {
                EditorGUILayout.HelpBox("Nested effect container is not an array.", MessageType.Warning);
                return;
            }
            if (depth > MaxNestedDepth)
            {
                EditorGUILayout.HelpBox("Nested effect depth limit reached (8).", MessageType.Warning);
                return;
            }

            EditorUIUtil.BoxScope(() =>
            {
                EditorUIUtil.Header(string.IsNullOrEmpty(header) ? "Nested Effects" : header,
                    $"{listProp.arraySize} item(s)");

                // 工具条：Add / Expand All / Collapse All
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("＋ Add Effect", GUILayout.Height(20)))
                {
                    int newIndex = listProp.arraySize;
                    listProp.InsertArrayElementAtIndex(newIndex);
                    var newElem = listProp.GetArrayElementAtIndex(newIndex);
                    EnsureEffectPropertyInitialized(newElem);
                    ResetEffectProperty(newElem);
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Expand All", GUILayout.Width(100)))
                    SetAllFoldouts(listProp, true);
                if (GUILayout.Button("Collapse All", GUILayout.Width(100)))
                    SetAllFoldouts(listProp, false);
                EditorGUILayout.EndHorizontal();

                EditorUIUtil.Separator();

                // 列表项
                for (int i = 0; i < listProp.arraySize; i++)
                {
                    var effectProp = listProp.GetArrayElementAtIndex(i);
                    if (effectProp == null) continue;

                    EnsureEffectPropertyInitialized(effectProp);
                    var typeProp = effectProp.FindPropertyRelative("effectType");
                    var type = (EffectType)(typeProp?.intValue ?? 0);

                    // 标题行：折叠 + 徽标 + 操作按钮
                    EditorGUILayout.BeginHorizontal();
                    string foldKey = $"{listProp.propertyPath}#{i}";
                    var badge = type.ToString();
                    var open = EditorUIUtil.Foldout(foldKey, $"Effect {i + 1}", defaultState: true);

                    // 右侧徽标
                    var rect = GUILayoutUtility.GetLastRect();
                    var c = EditorUIUtil.ColorForEffectType(type);
                    var tagRect = new Rect(rect.xMax - 80f, rect.y + 2f, 76f, rect.height - 4f);
                    if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(tagRect, c);
                    GUI.Label(tagRect, badge, EditorStyles.miniBoldLabel);

                    GUILayout.FlexibleSpace();
                    GUI.enabled = i > 0;
                    if (GUILayout.Button("▲", GUILayout.Width(24))) { listProp.MoveArrayElement(i, i - 1); GUIUtility.ExitGUI(); }
                    GUI.enabled = i < listProp.arraySize - 1;
                    if (GUILayout.Button("▼", GUILayout.Width(24))) { listProp.MoveArrayElement(i, i + 1); GUIUtility.ExitGUI(); }
                    GUI.enabled = true;
                    if (GUILayout.Button("X", GUILayout.Width(24)))
                    {
                        RemoveArrayElement(listProp, i);
                        GUIUtility.ExitGUI();
                    }
                    EditorGUILayout.EndHorizontal();

                    if (!open) { EditorUIUtil.Separator(0.5f, 2f); continue; }

                    // 内容体
                    EditorUIUtil.BoxScope(() =>
                    {
                        EditorGUILayout.PropertyField(typeProp, new GUIContent("Effect Type"));

                        var drawer = EffectDrawerRegistry.Get(type);
                        if (drawer == null)
                        {
                            EditorGUILayout.HelpBox($"No drawer registered for effect type {type}.", MessageType.Error);
                        }
                        else
                        {
                            using (new EditorGUI.IndentLevelScope())
                                drawer.Draw(effectProp);
                        }
                    });

                    EditorUIUtil.Separator();
                }
            });
        }

        private static void SetAllFoldouts(SerializedProperty array, bool open)
        {
            for (int i = 0; i < array.arraySize; i++)
            {
                string key = $"{array.propertyPath}#{i}";
                // 直接触发一次 Foldout 存储开关
                EditorUIUtil.Foldout(key, $"Effect {i + 1}", open);
            }
        }

        internal static void ClearArray(SerializedProperty prop)
        {
            if (prop == null || !prop.isArray)
                return;

            for (int i = prop.arraySize - 1; i >= 0; i--)
                RemoveArrayElement(prop, i);
        }

        internal static void RemoveArrayElement(SerializedProperty array, int index)
        {
            if (array == null || !array.isArray || index < 0 || index >= array.arraySize)
                return;

            array.DeleteArrayElementAtIndex(index);
            if (array.arraySize > index)
            {
                var element = array.GetArrayElementAtIndex(index);
                if (element != null && element.propertyType == SerializedPropertyType.ManagedReference && element.managedReferenceValue == null)
                    array.DeleteArrayElementAtIndex(index);
            }
        }

        private static void EnsureEffectPropertyInitialized(SerializedProperty effectProp)
        {
            if (effectProp == null)
                return;

            if (effectProp.propertyType == SerializedPropertyType.ManagedReference && effectProp.managedReferenceValue == null)
            {
                effectProp.managedReferenceValue = new EffectDefinition();
            }
        }

        private static void ResetEffectProperty(SerializedProperty effectProp)
        {
            if (effectProp == null) return;

            SetEnumValue(effectProp.FindPropertyRelative("effectType"), EffectType.None);
            SetEnumValue(effectProp.FindPropertyRelative("condition"), EffectCondition.None);
            SetEnumValue(effectProp.FindPropertyRelative("target"), TargetType.Self);
            SetEnumValue(effectProp.FindPropertyRelative("attributeType"), AttributeType.Attack);
            SetEnumValue(effectProp.FindPropertyRelative("damageSchool"), DamageSchool.Physical);
            SetEnumValue(effectProp.FindPropertyRelative("auraRangeMode"), AuraRangeMode.Within);
            SetEnumValue(effectProp.FindPropertyRelative("auraOnEnter"), EffectCondition.None);
            SetEnumValue(effectProp.FindPropertyRelative("auraOnExit"), EffectCondition.None);

            SetString(effectProp.FindPropertyRelative("valueExpression"), string.Empty);
            SetString(effectProp.FindPropertyRelative("probability"), string.Empty);
            SetString(effectProp.FindPropertyRelative("statusSkillID"), string.Empty);
            SetString(effectProp.FindPropertyRelative("targetSkillID"), string.Empty);
            SetString(effectProp.FindPropertyRelative("actionFilterTag"), string.Empty);
            SetString(effectProp.FindPropertyRelative("repeatCountExpression"), string.Empty);

            SetInt(effectProp.FindPropertyRelative("stackCount"), 1);
            SetFloat(effectProp.FindPropertyRelative("value"), 0f);
            SetFloat(effectProp.FindPropertyRelative("duration"), 0f);
            SetFloat(effectProp.FindPropertyRelative("auraMinRadius"), 0f);
            SetFloat(effectProp.FindPropertyRelative("auraMaxRadius"), 0f);
            SetInt(effectProp.FindPropertyRelative("repeatCount"), 1);
            SetInt(effectProp.FindPropertyRelative("repeatMaxCount"), 0);
            SetInt(effectProp.FindPropertyRelative("dotHotBaseTriggerCount"), 0);
            SetInt(effectProp.FindPropertyRelative("auraHeartSeconds"), 6);

            SetBool(effectProp.FindPropertyRelative("perLevel"), false);
            SetBool(effectProp.FindPropertyRelative("perLevelDuration"), true);
            SetBool(effectProp.FindPropertyRelative("repeatConsumeResource"), true);

            ClearArray(effectProp.FindPropertyRelative("onSuccess"));
            ClearArray(effectProp.FindPropertyRelative("repeatEffects"));
            ClearArray(effectProp.FindPropertyRelative("dotHotAdditionalEffects"));
            ClearArray(effectProp.FindPropertyRelative("auraAdditionalEffects"));
            ClearArray(effectProp.FindPropertyRelative("randomOutcomes"));
            ClearArray(effectProp.FindPropertyRelative("negativeStatuses"));
        }

        private static void SetEnumValue(SerializedProperty prop, Enum value)
        {
            if (prop == null) return;
            if (prop.propertyType == SerializedPropertyType.Enum)
                prop.intValue = Convert.ToInt32(value);
        }
        private static void SetString(SerializedProperty prop, string value) { if (prop != null) prop.stringValue = value ?? string.Empty; }
        private static void SetInt(SerializedProperty prop, int value) { if (prop != null) prop.intValue = value; }
        private static void SetFloat(SerializedProperty prop, float value) { if (prop != null) prop.floatValue = value; }
        private static void SetBool(SerializedProperty prop, bool value) { if (prop != null) prop.boolValue = value; }
    }
}
