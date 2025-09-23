using System.Collections.Generic;
using UnityEngine;     
using TGD.Data;

namespace TGD.Combat
{
    /// <summary>
    /// 一个“能跑起来”的默认实现：
    /// - 你可以构造时传入全量技能列表（Dictionary 或 IEnumerable）
    /// - 或者用 Resources 路径自动加载（仅用于测试/沙盒）
    /// </summary>
    public sealed class DefaultSkillResolver : ISkillResolver
    {
        private readonly IReadOnlyDictionary<string, SkillDefinition> _map;

        // 用字典直接注入（生产推荐）
        public DefaultSkillResolver(IReadOnlyDictionary<string, SkillDefinition> map)
        {
            _map = map ?? new Dictionary<string, SkillDefinition>();
        }

        // 用枚举注入（生产可用）
        public DefaultSkillResolver(IEnumerable<SkillDefinition> all)
        {
            var dict = new Dictionary<string, SkillDefinition>();
            if (all != null)
            {
                foreach (var s in all)
                {
                    if (s != null && !string.IsNullOrEmpty(s.skillID))
                        dict[s.skillID] = s;
                }
            }
            _map = dict;
        }

        // 用 Resources 自动加载（沙盒/测试推荐；生产可换 Addressables）
        public static DefaultSkillResolver FromResources(string resourcesFolder = "Skills")
        {
            var all = Resources.LoadAll<SkillDefinition>(resourcesFolder);
            return new DefaultSkillResolver(all);
        }

        public SkillDefinition ResolveById(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return null;
            return _map.TryGetValue(skillId, out var def) ? def : null;
        }
    }
}


