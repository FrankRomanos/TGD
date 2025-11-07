using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.DataV2
{
    /// <summary>
    /// 统一管理“动作冷却秒数”的小型目录。
    /// 放到 Resources/ActionCooldownCatalog.asset 后，代码里可通过 ActionCooldownCatalog.Instance 访问。
    /// 未配置的 key 返回 0（无冷却）。
    /// </summary>
    [CreateAssetMenu(menuName = "TGD/Catalogs/ActionCooldownCatalog", fileName = "ActionCooldownCatalog")]
    public sealed class ActionCooldownCatalog : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string key;     // 比如 "Move" / "Attack" / "ShieldBash" / "Fireball"
            [Min(0)] public int seconds;
        }

        [SerializeField] private List<Entry> entries = new();

        private Dictionary<string, int> _map;

        private static ActionCooldownCatalog _instance;
        public static ActionCooldownCatalog Instance
        {
            get
            {
                if (_instance != null) return _instance;

                // 约定资源路径：Resources/ActionCooldownCatalog.asset
                _instance = Resources.Load<ActionCooldownCatalog>("ActionCooldownCatalog");

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
            set { _instance = value; }
        }

        private void OnEnable() => Rebuild();

        private void Rebuild()
        {
            if (_map == null) _map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            else _map.Clear();

            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.key)) continue;
                _map[e.key] = Mathf.Max(0, e.seconds);
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
