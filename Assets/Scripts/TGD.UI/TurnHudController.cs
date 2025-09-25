using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;                 // 若 asmdef 未引入 TextMeshPro，可把 TMP_Text 换成 UnityEngine.UI.Text
using UnityEngine;
using UnityEngine.UI;
using TGD.Combat;
using TGD.Data;             // 用到 SkillDatabase、CostResourceType

namespace TGD.UI
{
    /// <summary>
    /// 底部 HUD（出手单位专用）：
    /// - 顶：通用能量条（Energy）
    /// - 下：职业资源条（由不同颜色的小长条拼接，自动从该职业技能里扫描出资源类型；排除 HP/Energy）
    /// - 可选：回合时间豆（维持你之前的小球逻辑）
    /// </summary>
    public class TurnHudController : MonoBehaviour
    {
        [Header("Refs")]
        public CombatLoop combat;                    // 可空，自动找
        public CanvasGroup canvasGroup;              // 渐隐控制

        [Header("Energy Bar")]
        public Slider energySlider;
        public TMP_Text energyText;                  // 仅显示当前值（想 cur/max 自行改）
        [Range(1f, 20f)] public float energyLerp = 10f;

        [Header("Class Resource Strip (colored segments)")]
        public Transform stripRoot;                  // 水平容器
        public GameObject stripCellPrefab;           // 小长条预制（Image，建议 18x8）
        [Range(0f, 1f)] public float offAlpha = 0.25f;
        public float groupGap = 8f;                  // 不同资源类型之间的空隙（Layout 里实现）

        [Header("Turn Time (optional)")]
        public bool showTimeBeans = true;
        public Transform timeRoot;                   // 水平容器
        public GameObject timePipPrefab;             // 小球预制（Image，建议 14x14）
        public Color timeTint = Color.white;
        [Range(0f, 1f)] public float timeOffAlpha = 0.25f;
        public TMP_Text timeText;

        [Header("Update")]
        public float updateInterval = 0.1f;          // 轮询刷新（没有事件总线就用它）

        // —— 运行态 —— //
        Unit _active;
        float _timer;

        struct Seg
        {
            public Image img;
            public Color color;
            public CostResourceType type;
            public int indexInType; // 同类型的第几个，用于点亮前 N 个
        }
        readonly List<Seg> _segments = new();        // 按显示顺序存所有小长条
        readonly List<(CostResourceType type, int max, int cur)> _typeSnapshot = new();

        Image[] _timePips = Array.Empty<Image>();
        int _timeLastMax = -1, _timeLastRemain = -1;

        // —— 可选：颜色自定义（Inspector 覆盖）—— //
        [Serializable] public class TypeColor { public CostResourceType type; public Color color = Color.white; }
        public List<TypeColor> paletteOverride = new();

        void Awake()
        {
            if (!combat) combat = FindFirstObjectByTypeSafe<CombatLoop>();
            Show(false, true);

            if (combat != null)
            {
                combat.OnTurnBegan += OnTurnBegan;
                combat.OnTurnEnded += _ => Show(false);
            }
        }
        void OnDestroy()
        {
            if (combat != null)
            {
                combat.OnTurnBegan -= OnTurnBegan;
                combat.OnTurnEnded -= _ => Show(false);
            }
        }

        void OnTurnBegan(Unit u)
        {
            _active = u;
            RebuildStrip();
            RebuildTimeRow();
            RefreshImmediate();
            Show(true);
        }

        void Update()
        {
            if (_active == null) return;
            _timer += Time.deltaTime;
            if (_timer >= updateInterval)
            {
                _timer = 0f;
                RefreshStep();
            }
        }

        // ----------------- 构建 UI -----------------

        void RebuildStrip()
        {
            // 清空
            for (int i = stripRoot.childCount - 1; i >= 0; --i)
                Destroy(stripRoot.GetChild(i).gameObject);
            _segments.Clear();

            if (_active == null) return;

            // 1) 扫描该职业技能 → 资源类型集合（排除 HP/Energy）
            var types = CollectTypesFromSkills(_active.ClassId);

            // 2) 按资源类型依次创建小长条
            bool firstGroup = true;
            foreach (var t in types)
            {
                // 取当前值/上限（从 Stats 里反射；若无上限字段，用默认）
                var key = ToStatsKey(t);
                int max = Mathf.Max(1, GetStatMaxInt(_active.Stats, key, GetDefaultMax(t)));
                int cur = Mathf.Clamp(GetStatInt(_active.Stats, key), 0, max);
                _typeSnapshot.Add((t, max, cur));

                // 组间空隙：插入一个空 GameObject（加 LayoutElement minWidth = groupGap）
                if (!firstGroup && groupGap > 0.01f)
                {
                    var gapGO = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement));
                    gapGO.transform.SetParent(stripRoot, false);
                    gapGO.GetComponent<LayoutElement>().minWidth = groupGap;
                }
                firstGroup = false;

                // 创建 max 个 segment
                for (int i = 0; i < max; i++)
                {
                    var cell = Instantiate(stripCellPrefab, stripRoot);
                    var img = cell.GetComponent<Image>();
                    var col = GetColor(t); col.a = (i < cur) ? 1f : offAlpha;
                    if (img) img.color = col;

                    _segments.Add(new Seg { img = img, color = GetColor(t), type = t, indexInType = i });
                }
            }
        }

        void RebuildTimeRow()
        {
            // 清空
            for (int i = timeRoot.childCount - 1; i >= 0; --i)
                Destroy(timeRoot.GetChild(i).gameObject);
            _timePips = Array.Empty<Image>();
            _timeLastMax = -1; _timeLastRemain = -1;

            if (!showTimeBeans || _active == null) return;
            int total = Mathf.Max(1, _active.TurnTime);
            _timePips = new Image[total];
            for (int i = 0; i < total; i++)
            {
                var pip = Instantiate(timePipPrefab, timeRoot);
                var img = pip.GetComponent<Image>();
                if (img)
                {
                    var c = timeTint; c.a = timeOffAlpha;
                    img.color = c;
                    _timePips[i] = img;
                }
            }
            _timeLastMax = total;
        }

        // ----------------- 刷新 -----------------

        void RefreshImmediate()
        {
            if (_active?.Stats == null) return;

            // 能量
            float cur = TryGetFloat(_active.Stats, "Energy");
            float max = Mathf.Max(1f, TryGetFloat(_active.Stats, "MaxEnergy", 100f));
            if (energySlider) energySlider.value = Mathf.Clamp01(cur / max);
            if (energyText) energyText.text = $"{Mathf.RoundToInt(cur)}";

            UpdateStrip(true);
            UpdateTime(true);
        }

        void RefreshStep()
        {
            if (_active?.Stats == null) return;

            // 能量（平滑）
            float cur = TryGetFloat(_active.Stats, "Energy");
            float max = Mathf.Max(1f, TryGetFloat(_active.Stats, "MaxEnergy", 100f));
            if (energySlider) energySlider.value = Mathf.Lerp(energySlider.value, Mathf.Clamp01(cur / max), Time.deltaTime * energyLerp);
            if (energyText) energyText.text = $"{Mathf.RoundToInt(cur)}";

            // 资源条：若某资源上限变更（例如 BUFF），需要重建
            bool needRebuild = false;
            foreach (var (type, lastMax, _) in _typeSnapshot)
            {
                var key = ToStatsKey(type);
                int nowMax = Mathf.Max(1, GetStatMaxInt(_active.Stats, key, GetDefaultMax(type)));
                if (nowMax != lastMax) { needRebuild = true; break; }
            }
            if (needRebuild)
            {
                _typeSnapshot.Clear();
                RebuildStrip();
            }
            UpdateStrip(false);

            // 时间豆：TurnTime 改变 → 重建
            if (showTimeBeans)
            {
                int total = Mathf.Max(1, _active.TurnTime);
                if (total != _timeLastMax) RebuildTimeRow();
                UpdateTime(false);
            }
        }

        void UpdateStrip(bool force)
        {
            if (_segments.Count == 0) return;

            // 计算每个类型当前值
            var curByType = new Dictionary<CostResourceType, int>();
            foreach (var (type, max, _) in _typeSnapshot)
            {
                var key = ToStatsKey(type);
                int cur = Mathf.Clamp(GetStatInt(_active.Stats, key), 0, max);
                curByType[type] = cur;
            }

            // 点亮前 N 个
            foreach (var seg in _segments)
            {
                if (!seg.img) continue;
                int cur = curByType.TryGetValue(seg.type, out var v) ? v : 0;
                var c = seg.color; c.a = (seg.indexInType < cur) ? 1f : offAlpha;
                seg.img.color = c;
            }
        }

        void UpdateTime(bool force)
        {
            if (_timePips == null || _timePips.Length == 0) return;

            int remain = Mathf.Clamp(_active.RemainingTime, 0, _timePips.Length);
            if (!force && remain == _timeLastRemain) return;
            _timeLastRemain = remain;

            for (int i = 0; i < _timePips.Length; i++)
            {
                var img = _timePips[i];
                if (!img) continue;
                var c = timeTint; c.a = (i < remain) ? 1f : timeOffAlpha;
                img.color = c;
            }
            if (timeText) timeText.text = $"{remain}";
        }

        // ----------------- 工具：扫描/反射/颜色 -----------------

        // 扫描该职业技能，返回去重后的资源类型列表（排除 HP/Energy）
        List<CostResourceType> CollectTypesFromSkills(string classId)
        {
            var set = new HashSet<CostResourceType>();
            var skills = SkillDatabase.GetSkillsForClass(classId);
            if (skills != null)
            {
                foreach (var s in skills)
                {
                    if (s?.costs == null) continue;
                    foreach (var c in s.costs)
                    {
                        var t = c.resourceType;
                        if (t == CostResourceType.HP || t == CostResourceType.Energy) continue;
                        set.Add(t);
                    }
                }
            }
            var list = new List<CostResourceType>(set);
            // 固定一个稳定顺序（枚举定义顺序）
            list.Sort((a, b) => ((int)a).CompareTo((int)b));
            return list;
        }

        // 把枚举转成 Stats 字段名（假定你的 Stats 成员跟这些名一致，如 Rage/Combo/Qi/Versatility 等）
        static string ToStatsKey(CostResourceType t)
        {
            // 直接用枚举名，首字母大写即可（已是 PascalCase 的直接返回）
            string s = t.ToString();      // Rage / Versatility / Gunpowder / combo / qi ...
            if (s.Length == 0) return s;
            // 统一成首字母大写，其余维持原样
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        // 默认上限（当 Stats 上找不到 *Max 时）
        static int GetDefaultMax(CostResourceType t) => t switch
        {
            CostResourceType.Rage => 100,
            CostResourceType.combo => 5,
            CostResourceType.point => 3,
            CostResourceType.qi or CostResourceType.qi => 4,
            CostResourceType.posture => 4,
            _ => 5
        };

        // 颜色：可在 Inspector 里 paletteOverride 覆盖
        Color GetColor(CostResourceType t)
        {
            var o = paletteOverride.Find(p => p.type.Equals(t));
            if (o != null) return o.color;

            return t switch
            {
                CostResourceType.Discipline => new Color(0.18f, 0.6f, 1f),   // 深蓝
                CostResourceType.Iron => new Color(0.6f, 0.6f, 0.65f), // 铁灰
                CostResourceType.Rage => new Color(1f, 0.2f, 0.2f),    // 红
                CostResourceType.Versatility => new Color(1f, 0.7f, 0.15f),   // 深黄
                CostResourceType.Gunpowder => new Color(0.55f, 0.4f, 0.2f), // 棕
                CostResourceType.point => new Color(1f, 0.35f, 0.35f),  // 红点
                CostResourceType.combo => new Color(1f, 1f, 1f),        // 白
                CostResourceType.punch => new Color(1f, 0.5f, 0.1f),    // 橙
                CostResourceType.qi => new Color(0.2f, 0.9f, 0.9f),   // 青
                CostResourceType.vision => new Color(0.7f, 0.5f, 1f),    // 紫
                CostResourceType.posture => new Color(0.25f, 0.9f, 0.25f),// 绿
                _ => new Color(0.8f, 0.8f, 0.8f)
            };
        }

        // 反射：读 Stats 上当前值 / 上限
        static float TryGetFloat(object stats, string key, float fallback = 0f)
        {
            if (stats == null) return fallback;
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            var p = stats.GetType().GetProperty(key, flags);
            if (p != null)
            {
                var v = p.GetValue(stats);
                if (v is float f) return f;
                if (v is int i) return i;
            }
            var f1 = stats.GetType().GetField(key, flags);
            if (f1 != null)
            {
                var v = f1.GetValue(stats);
                if (v is float f) return f;
                if (v is int i) return i;
            }
            return fallback;
        }
        static int GetStatInt(object stats, string key) => Mathf.RoundToInt(TryGetFloat(stats, key, 0f));

        static int GetStatMaxInt(object stats, string key, int fallback)
        {
            string[] cand =
            {
                $"{key}Max", $"Max{key}", $"{key}_Max", $"max{key}", $"{FirstUpper(key)}Cap", $"{key}Cap"
            };
            foreach (var name in cand)
            {
                float v = TryGetFloat(stats, name, float.NaN);
                if (!float.IsNaN(v)) return Mathf.Max(1, Mathf.RoundToInt(v));
            }
            return fallback;
        }
        static string FirstUpper(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

        // 2023+ 兼容
        static T FindFirstObjectByTypeSafe<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<T>();
#endif
        }

        // 显隐
        void Show(bool on, bool instant = false)
        {
            if (!canvasGroup) return;
            if (instant) canvasGroup.alpha = on ? 1f : 0f;
            else StartCoroutine(FadeTo(on ? 1f : 0f));
            canvasGroup.interactable = on;
            canvasGroup.blocksRaycasts = on;
        }
        System.Collections.IEnumerator FadeTo(float t)
        {
            while (!Mathf.Approximately(canvasGroup.alpha, t))
            {
                canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, t, Time.deltaTime * 8f);
                yield return null;
            }
        }
    }
}
