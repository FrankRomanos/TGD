using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.DataV2
{
    [CreateAssetMenu(menuName = "TGD/Skills/SkillIndex", fileName = "SkillIndex")]
    public sealed class SkillIndex : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public SkillDefinitionV2 definition;
            [Tooltip("Optional override. Empty uses definition DisplayName.")]
            public string displayName;
            [Tooltip("Optional override. Null uses definition Icon.")]
            public Sprite icon;
        }

        [Serializable]
        public struct SkillInfo
        {
            public readonly SkillDefinitionV2 definition;
            public readonly string displayName;
            public readonly Sprite icon;

            public SkillInfo(SkillDefinitionV2 definition, string displayName, Sprite icon)
            {
                this.definition = definition;
                this.displayName = displayName;
                this.icon = icon;
            }
        }

        [SerializeField]
        public List<Entry> entries = new();

        private readonly Dictionary<string, SkillInfo> _map = new(StringComparer.OrdinalIgnoreCase);

        public bool Contains(string id) => TryGet(id, out _);

        public bool TryGet(string id, out SkillInfo info)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                info = default;
                return false;
            }

            if (_map.TryGetValue(Normalize(id), out info))
                return true;

            Rebuild();
            return _map.TryGetValue(Normalize(id), out info);
        }

        public IEnumerable<SkillInfo> All()
        {
            Rebuild();
            return _map.Values;
        }

        private void OnEnable() => Rebuild();

#if UNITY_EDITOR
        private void OnValidate() => Rebuild();
#endif

        private void Rebuild()
        {
            _map.Clear();

            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.definition == null)
                    continue;

                var id = entry.definition.Id;
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var display = string.IsNullOrWhiteSpace(entry.displayName)
                    ? entry.definition.DisplayName
                    : entry.displayName.Trim();

                var icon = entry.icon != null ? entry.icon : entry.definition.Icon;
                _map[id] = new SkillInfo(entry.definition, display, icon);
            }
        }

        private static string Normalize(string id)
            => string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
    }
}
