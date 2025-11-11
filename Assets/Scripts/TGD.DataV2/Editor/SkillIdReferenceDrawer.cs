#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TGD.DataV2.Editor
{
    [CustomPropertyDrawer(typeof(SkillIdReferenceAttribute))]
    internal sealed class SkillIdReferenceDrawer : PropertyDrawer
    {
        private const float ButtonWidth = 22f;
        private static readonly List<SkillEntry> _entries = new();
        private static readonly Dictionary<string, SkillEntry> _entryMap = new(StringComparer.OrdinalIgnoreCase);
        private static double _lastRefreshTime;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            RefreshIfNeeded();

            float height = EditorGUIUtility.singleLineHeight;
            bool allowEmpty = (attribute as SkillIdReferenceAttribute)?.allowEmpty ?? true;
            string normalized = SkillDisplayNameUtility.NormalizeId(property.stringValue);
            bool hasValue = !string.IsNullOrEmpty(normalized);
            bool hasEntry = hasValue && _entryMap.ContainsKey(normalized);
            bool isValid = !hasValue ? allowEmpty : hasEntry;

            if (hasEntry || (!isValid && hasValue))
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUIUtility.singleLineHeight;

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            RefreshIfNeeded();

            bool allowEmpty = (attribute as SkillIdReferenceAttribute)?.allowEmpty ?? true;
            string normalized = SkillDisplayNameUtility.NormalizeId(property.stringValue);
            bool hasValue = !string.IsNullOrEmpty(normalized);
            SkillEntry entry = default;
            bool hasEntry = hasValue && _entryMap.TryGetValue(normalized, out entry);
            bool isValid = !hasValue ? allowEmpty : hasEntry;

            Rect fieldRect = EditorGUI.PrefixLabel(position, label);
            Rect buttonRect = fieldRect;
            buttonRect.width = ButtonWidth;
            buttonRect.x = fieldRect.xMax - ButtonWidth;
            Rect textRect = fieldRect;
            textRect.width -= ButtonWidth + 2f;
            if (textRect.width < 0f)
                textRect.width = 0f;

            EditorGUI.BeginChangeCheck();
            string newValue = EditorGUI.TextField(textRect, property.stringValue);
            if (EditorGUI.EndChangeCheck())
            {
                property.stringValue = newValue;
                normalized = SkillDisplayNameUtility.NormalizeId(newValue);
                hasValue = !string.IsNullOrEmpty(normalized);
                hasEntry = hasValue && _entryMap.TryGetValue(normalized, out entry);
                isValid = !hasValue ? allowEmpty : hasEntry;
            }

            using (new EditorGUI.DisabledScope(_entries.Count == 0))
            {
                if (GUI.Button(buttonRect, new GUIContent("⋯", "Select from SkillIndex")))
                {
                    ShowMenu(property, buttonRect);
                }
            }

            Rect infoRect = new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing,
                position.width, EditorGUIUtility.singleLineHeight);

            if (hasEntry)
            {
                EditorGUI.LabelField(infoRect, new GUIContent($"{entry.DisplayName} ({entry.Id})", "Display name resolved from SkillIndex."), EditorStyles.miniLabel);
            }
            else if (hasValue && !isValid)
            {
                EditorGUI.HelpBox(infoRect, "Skill not found in SkillIndex.", MessageType.Warning);
            }

            EditorGUI.EndProperty();
        }

        private static void ShowMenu(SerializedProperty property, Rect position)
        {
            var menu = new GenericMenu();
            var so = property.serializedObject;
            string path = property.propertyPath;

            bool isEmpty = string.IsNullOrEmpty(SkillDisplayNameUtility.NormalizeId(property.stringValue));
            menu.AddItem(new GUIContent("Clear"), isEmpty, () =>
            {
                var targetProperty = so.FindProperty(path);
                if (targetProperty == null)
                    return;
                targetProperty.stringValue = string.Empty;
                so.ApplyModifiedProperties();
            });

            if (_entries.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No SkillIndex assets found"));
            }
            else
            {
                menu.AddSeparator("");
                foreach (var entry in _entries)
                {
                    bool on = string.Equals(SkillDisplayNameUtility.NormalizeId(property.stringValue), entry.Id, StringComparison.OrdinalIgnoreCase);
                    menu.AddItem(new GUIContent($"{entry.DisplayName} ({entry.Id})"), on, () =>
                    {
                        var targetProperty = so.FindProperty(path);
                        if (targetProperty == null)
                            return;
                        targetProperty.stringValue = entry.Id;
                        so.ApplyModifiedProperties();
                    });
                }
            }

            menu.DropDown(position);
        }

        private static void RefreshIfNeeded()
        {
            double now = EditorApplication.timeSinceStartup;
            if (_entries.Count > 0 && now - _lastRefreshTime < 1.0)
                return;

            _lastRefreshTime = now;
            _entries.Clear();
            _entryMap.Clear();

            string[] guids = AssetDatabase.FindAssets("t:SkillIndex");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var index = AssetDatabase.LoadAssetAtPath<SkillIndex>(assetPath);
                if (index == null || index.entries == null)
                    continue;

                foreach (var entry in index.entries)
                {
                    if (entry.definition == null)
                        continue;

                    string id = SkillDisplayNameUtility.NormalizeId(entry.definition.Id);
                    if (string.IsNullOrEmpty(id))
                        continue;

                    if (!seen.Add(id))
                        continue;

                    string display = SkillDisplayNameUtility.ResolveDisplayName(id, index, entry.definition);
                    var record = new SkillEntry(id, string.IsNullOrEmpty(display) ? id : display);
                    _entries.Add(record);
                    _entryMap[id] = record;
                }
            }

            _entries.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        }

        private readonly struct SkillEntry
        {
            public string Id { get; }
            public string DisplayName { get; }

            public SkillEntry(string id, string displayName)
            {
                Id = id;
                DisplayName = displayName;
            }
        }
    }
}
#endif
