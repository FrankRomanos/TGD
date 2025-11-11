using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.DataV2
{
    /// <summary>
    /// 统一管理“动作冷却秒数”的小型目录。
    /// 实例可由工厂注入；若未显式注入，会在 Resources 中尝试自动定位一个资产。
    /// 未配置的 key 返回 0（无冷却）。
    /// </summary>
    [CreateAssetMenu(menuName = "TGD/Catalogs/ActionCooldownCatalog", fileName = "ActionCooldownCatalog")]
    public sealed class ActionCooldownCatalog : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [SkillIdReference]
            public string key;     // 比如 "Move" / "Attack" / "ShieldBash" / "Fireball"
            [Min(0)] public int seconds;
        }

        [SerializeField] private List<Entry> entries = new();

        private Dictionary<string, int> _map;

        private static ActionCooldownCatalog _instance;
        public static bool HasInstance => _instance != null;
        public static ActionCooldownCatalog Current => _instance;
        public static ActionCooldownCatalog Instance
        {
            get
            {
                if (_instance != null) return _instance;

                _instance = LocateInResources();

#if UNITY_EDITOR
                // 编辑器下没有资源也不阻塞运行：给一个内存默认体，Move/Attack=0
                if (_instance == null)
                {
                    _instance = CreateInstance<ActionCooldownCatalog>();
                    _instance.hideFlags = HideFlags.DontSave;
                    _instance.entries = new List<Entry>
                    {
                        new Entry{ key = "Move", seconds = 0 },
                        new Entry{ key = "Attack", seconds = 0 },
                    };
                    _instance.Rebuild();
                }
#endif
                if (_instance != null)
                    _instance.Rebuild();

                return _instance;
            }
            set
            {
                _instance = value;
                if (_instance != null)
                    _instance.Rebuild();
            }
        }

        private void OnEnable() => Rebuild();

        static ActionCooldownCatalog LocateInResources()
        {
            var direct = Resources.Load<ActionCooldownCatalog>("ActionCooldownCatalog");
            if (direct != null)
                return direct;

            var all = Resources.LoadAll<ActionCooldownCatalog>(string.Empty);
            if (all != null && all.Length > 0)
                return all[0];

            return null;
        }

        private void Rebuild()
        {
            if (_map == null) _map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            else _map.Clear();

            foreach (var e in entries)
            {
                string key = SkillDisplayNameUtility.NormalizeId(e.key);
                if (string.IsNullOrEmpty(key)) continue;
                _map[key] = Mathf.Max(0, e.seconds);
            }
        }

        public int GetSeconds(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return 0;
            if (_map != null && _map.TryGetValue(key, out var v)) return Mathf.Max(0, v);
            return 0;
        }

        // 冷却目录是唯一真相源：不允许运行时写回。
    }
}
