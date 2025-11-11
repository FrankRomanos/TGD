using UnityEngine;

namespace TGD.DataV2
{
    public static class SkillDisplayNameUtility
    {
        public static string NormalizeId(string skillId)
        {
            return string.IsNullOrWhiteSpace(skillId) ? string.Empty : skillId.Trim();
        }

        public static string ResolveDisplayName(string skillId, SkillIndex index = null, SkillDefinitionV2 fallbackDefinition = null)
        {
            string normalized = NormalizeId(skillId);
            if (string.IsNullOrEmpty(normalized))
                return string.Empty;

            if (index != null && index.TryGet(normalized, out var info))
            {
                if (!string.IsNullOrWhiteSpace(info.displayName))
                    return info.displayName;

                if (info.definition != null && !string.IsNullOrWhiteSpace(info.definition.DisplayName))
                    return info.definition.DisplayName;
            }

            if (fallbackDefinition != null && !string.IsNullOrWhiteSpace(fallbackDefinition.DisplayName))
                return fallbackDefinition.DisplayName;

            return normalized;
        }
    }
}
