using System.Collections.Generic;

namespace TGD.CoreV2.Rules
{
    public sealed class UnitRuleSet
    {
        readonly List<IRuleModifier> _mods = new(16);

        public void Add(IRuleModifier mod) { if (mod != null) _mods.Add(mod); }
        public void Remove(IRuleModifier mod) { if (mod != null) _mods.Remove(mod); }
        public void Clear() => _mods.Clear();

        public IReadOnlyList<IRuleModifier> Items => _mods;

        // 小工具：按优先级排序后的缓存（当前规模可不做缓存）
        public IEnumerable<T> Enumerate<T>() where T : class, IRuleModifier
        {
            // 避免频繁 Linq，直接两次循环
            for (int pass = 3; pass >= -3; --pass)
                foreach (var m in _mods)
                    if (m is T t && m.Priority == pass)
                        yield return t;
            foreach (var m in _mods)
                if (m is T t2 && (m.Priority > 3 || m.Priority < -3))
                    yield return t2;
        }
    }
}
