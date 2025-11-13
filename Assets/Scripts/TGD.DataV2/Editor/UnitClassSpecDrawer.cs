#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace TGD.DataV2.Editor
{
    [CustomPropertyDrawer(typeof(UnitBlueprint.ClassSpec))]
    public sealed class UnitClassSpecDrawer : PropertyDrawer
    {
        readonly struct SpecializationOption
        {
            public readonly string Id;
            public readonly string Display;

            public SpecializationOption(string id, string display)
            {
                Id = id;
                Display = display;
            }
        }

        readonly struct ProfessionOption
        {
            public readonly string Id;
            public readonly string Display;
            public readonly SpecializationOption[] Specializations;

            public ProfessionOption(string id, string display, SpecializationOption[] specializations)
            {
                Id = id;
                Display = display;
                Specializations = specializations ?? Array.Empty<SpecializationOption>();
            }
        }

        static readonly ProfessionOption[] s_professions =
        {
            new ProfessionOption(
                "Knight",
                "Knight",
                new[]
                {
                    new SpecializationOption("CL001", "CL001 · Knight1 王宫骑士"),
                    new SpecializationOption("CL002", "CL002 · Knight2 白骑士"),
                    new SpecializationOption("CL003", "CL003 · Knight3 流浪骑士")
                }),
            new ProfessionOption(
                "Samurai",
                "Samurai",
                new[]
                {
                    new SpecializationOption("CL011", "CL011 · Samurai1 武士"),
                    new SpecializationOption("CL012", "CL012 · Samurai2 师范"),
                    new SpecializationOption("CL013", "CL013 · Samurai3 剑圣")
                }),
            new ProfessionOption(
                "Warrior",
                "Warrior",
                new[]
                {
                    new SpecializationOption("CL021", "CL021 · Warrior1 狂战士"),
                    new SpecializationOption("CL022", "CL022 · Warrior2 佣兵")
                }),
            new ProfessionOption(
                "Pirate",
                "Pirate",
                new[]
                {
                    new SpecializationOption("CL031", "CL031 · Pirate1 赌棍"),
                    new SpecializationOption("CL032", "CL032 · Pirate2 决斗家")
                }),
            new ProfessionOption(
                "Alchemist",
                "Alchemist",
                new[]
                {
                    new SpecializationOption("CL041", "CL041 · Alchemist1 处刑人"),
                    new SpecializationOption("CL042", "CL042 · Alchemist2 化学家")
                }),
            new ProfessionOption(
                "Master",
                "Master",
                new[]
                {
                    new SpecializationOption("CL051", "CL051 · Master1 武术家"),
                    new SpecializationOption("CL052", "CL052 · Master2 白神"),
                    new SpecializationOption("CL053", "CL053 · Master3 气功师")
                }),
            new ProfessionOption(
                "Artist",
                "Artist",
                new[]
                {
                    new SpecializationOption("CL071", "CL071 · Artist1 宫廷画师")
                })
        };

        static readonly GUIContent s_emptyOption = new GUIContent("(None)");

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property == null)
            {
                EditorGUI.LabelField(position, label, new GUIContent("Property missing"));
                return;
            }

            var professionProp = property.FindPropertyRelative("professionId");
            var specializationProp = property.FindPropertyRelative("specializationId");

            EditorGUI.BeginProperty(position, label, property);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            var labelRect = new Rect(position.x, position.y, position.width, lineHeight);
            EditorGUI.LabelField(labelRect, label);

            EditorGUI.indentLevel++;

            var professionRect = new Rect(position.x, labelRect.y + lineHeight + spacing, position.width, lineHeight);
            DrawProfessionPopup(professionRect, professionProp, specializationProp);

            var specializationRect = new Rect(position.x, professionRect.y + lineHeight + spacing, position.width, lineHeight);
            DrawSpecializationPopup(specializationRect, professionProp, specializationProp);

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            return lineHeight * 3f + spacing * 2f;
        }

        void DrawProfessionPopup(Rect rect, SerializedProperty professionProp, SerializedProperty specializationProp)
        {
            string currentId = professionProp != null ? professionProp.stringValue : string.Empty;
            string[] display = new string[s_professions.Length + 1];
            display[0] = s_emptyOption.text;
            for (int i = 0; i < s_professions.Length; i++)
                display[i + 1] = s_professions[i].Display;

            int currentIndex = 0;
            if (!string.IsNullOrEmpty(currentId))
            {
                for (int i = 0; i < s_professions.Length; i++)
                {
                    if (string.Equals(s_professions[i].Id, currentId, StringComparison.OrdinalIgnoreCase))
                    {
                        currentIndex = i + 1;
                        break;
                    }
                }
            }

            int newIndex = EditorGUI.Popup(rect, new GUIContent("Profession"), currentIndex, display);
            if (newIndex == currentIndex)
                return;

            string newId = newIndex <= 0 ? string.Empty : s_professions[newIndex - 1].Id;
            if (professionProp != null)
                professionProp.stringValue = newId;

            if (specializationProp != null)
            {
                specializationProp.stringValue = ResolveDefaultSpecialization(newId);
            }
        }

        void DrawSpecializationPopup(Rect rect, SerializedProperty professionProp, SerializedProperty specializationProp)
        {
            string professionId = professionProp != null ? professionProp.stringValue : string.Empty;
            var options = ResolveSpecializations(professionId);
            string[] display = new string[options.Length + 1];
            display[0] = s_emptyOption.text;
            for (int i = 0; i < options.Length; i++)
                display[i + 1] = options[i].Display;

            string currentId = specializationProp != null ? specializationProp.stringValue : string.Empty;
            int currentIndex = 0;
            if (!string.IsNullOrEmpty(currentId))
            {
                for (int i = 0; i < options.Length; i++)
                {
                    if (string.Equals(options[i].Id, currentId, StringComparison.OrdinalIgnoreCase))
                    {
                        currentIndex = i + 1;
                        break;
                    }
                }
            }

            using (new EditorGUI.DisabledScope(options.Length == 0))
            {
                int newIndex = EditorGUI.Popup(rect, new GUIContent("Specialization"), currentIndex, display);
                if (newIndex != currentIndex && specializationProp != null)
                {
                    specializationProp.stringValue = newIndex <= 0 ? string.Empty : options[newIndex - 1].Id;
                }
            }
        }

        static string ResolveDefaultSpecialization(string professionId)
        {
            if (string.IsNullOrEmpty(professionId))
                return string.Empty;

            var specializations = ResolveSpecializations(professionId);
            return specializations.Length > 0 ? specializations[0].Id : string.Empty;
        }

        static SpecializationOption[] ResolveSpecializations(string professionId)
        {
            if (string.IsNullOrEmpty(professionId))
                return Array.Empty<SpecializationOption>();

            for (int i = 0; i < s_professions.Length; i++)
            {
                if (string.Equals(s_professions[i].Id, professionId, StringComparison.OrdinalIgnoreCase))
                    return s_professions[i].Specializations ?? Array.Empty<SpecializationOption>();
            }

            return Array.Empty<SpecializationOption>();
        }
    }
}
#endif
