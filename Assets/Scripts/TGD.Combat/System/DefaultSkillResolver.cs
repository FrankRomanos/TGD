using System.Collections.Generic;
using UnityEngine;     
using TGD.Data;

namespace TGD.Combat
{
    /// <summary>
    /// һ����������������Ĭ��ʵ�֣�
    /// - ����Թ���ʱ����ȫ�������б�Dictionary �� IEnumerable��
    /// - ������ Resources ·���Զ����أ������ڲ���/ɳ�У�
    /// </summary>
    public sealed class DefaultSkillResolver : ISkillResolver
    {
        private readonly IReadOnlyDictionary<string, SkillDefinition> _map;

        // ���ֵ�ֱ��ע�루�����Ƽ���
        public DefaultSkillResolver(IReadOnlyDictionary<string, SkillDefinition> map)
        {
            _map = map ?? new Dictionary<string, SkillDefinition>();
        }

        // ��ö��ע�루�������ã�
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

        // �� Resources �Զ����أ�ɳ��/�����Ƽ��������ɻ� Addressables��
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


