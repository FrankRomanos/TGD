using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.Data
{
    public static class SkillDatabase
    {
        private static readonly Dictionary<string, SkillDefinition> SkillsById = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<SkillDefinition>> SkillsByClass = new(StringComparer.OrdinalIgnoreCase);
        private static bool _initialized;

        public static void EnsureLoaded(string resourcePath = "SkillData")
        {
            if (_initialized)
                return;

            SkillsById.Clear();
            SkillsByClass.Clear();

            var assets = Resources.LoadAll<SkillDefinition>(resourcePath);
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning($"[SkillDatabase] No SkillDefinition assets found under Resources/{resourcePath}.");
            }
            else
            {
                foreach (var asset in assets)
                {
                    if (asset == null)
                        continue;

                    try
                    {
                        var skill = ScriptableObject.Instantiate(asset);
                        skill.name = asset.name;
                        PostProcess(skill);
                        Register(skill);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Failed to clone skill asset '{asset.name}': {ex.Message}");
                    }
                }
            }

            _initialized = true;
        }

        public static SkillDefinition GetSkillById(string skillId)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(skillId))
                return null;
            return SkillsById.TryGetValue(skillId, out var value) ? value : null;
        }

        public static IReadOnlyList<SkillDefinition> GetSkillsForClass(string classId)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(classId))
                return Array.Empty<SkillDefinition>();
            return SkillsByClass.TryGetValue(classId, out var list) ? list : Array.Empty<SkillDefinition>();
        }

        public static IReadOnlyList<SkillDefinition> GetAllSkills()
        {
            EnsureLoaded();
            return new List<SkillDefinition>(SkillsById.Values);
        }

        private static void Register(SkillDefinition skill)
        {
            if (skill == null || string.IsNullOrWhiteSpace(skill.skillID))
                return;

            SkillsById[skill.skillID] = skill;
            if (!SkillsByClass.TryGetValue(skill.classID ?? string.Empty, out var list))
            {
                list = new List<SkillDefinition>();
                SkillsByClass[skill.classID ?? string.Empty] = list;
            }

            if (!list.Contains(skill))
                list.Add(skill);
        }

        private static void PostProcess(SkillDefinition skill)
        {
            if (skill == null)
                return;
            if (skill.skillDuration == null)
                skill.skillDuration = new SkillDurationSettings();
            if (skill.statusMetadata == null)
                skill.statusMetadata = new StatusSkillMetadata();
            skill.statusMetadata.EnsureInitialized();
            if (!string.IsNullOrWhiteSpace(skill.skillID))
                skill.name = skill.skillID;
        }
    }
}
