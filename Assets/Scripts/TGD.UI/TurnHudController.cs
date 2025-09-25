// Assets/Scripts/TGD.UI/TurnHudController.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TGD.Combat;
using TGD.Data;

namespace TGD.UI
{
    /// <summary>
    /// 出手单位 HUD：
    /// 顶：能量条（显示：当前/最大 + EnergyRegenPer2s）
    /// 中：职业资源彩条（自动扫描该职业技能里的资源类型，排除 HP/Energy）
    /// 底：回合时间豆（TurnTime 个小图标，点亮 RemainingTime）
    /// </summary>
    public sealed class TurnHudController : BaseTurnUiBehaviour
    {
        [Header("Canvas Group")]
        public CanvasGroup canvasGroup;

        [Header("Energy")]
        public Slider energySlider;
        public TMP_Text energyText;                 // “cur/max  +regen/2s”
        [Range(1f, 20f)] public float energyLerp = 10f;

        [Header("Class Resource Strip")]
        public Transform stripRoot;                 // 水平容器
        public GameObject stripCellPrefab;          // 小长条预制（Image，建议 18x8）
        [Range(0f, 1f)] public float offAlpha = 0.25f;
        public float groupGap = 8f;

        [Header("Turn Time (beans)")]
        public Transform timeRoot;                  // 水平容器
        public GameObject timePipPrefab;            // 小球预制（Image，建议 14x14）
        public Color timeTint = Color.white;
        [Range(0f, 1f)] public float timeOffAlpha = 0.25f;
        public TMP_Text timeText;                   // 可选，显示数字

        [Header("Update")]
        public float updateInterval = 0.1f;

        // 运行态
        Unit _active;
        float _timer;

        struct Seg
        {
            public Image img;
            public Color color;
            public CostResourceType type;
            public int indexInType;
        }
        readonly List<Seg> _segments = new();
        readonly List<(CostResourceType type, int max, int cur)> _typeSnapshot = new();

        Image[] _timePips = Array.Empty<Image>();
        int _timeLastMax = -1, _timeLastRemain = -1;

        [Serializable] public class TypeColor { public CostResourceType type; public Color color = Color.white; }
        public List<TypeColor> paletteOverride = new();

        protected override void Awake()
        {
            base.Awake();
            Show(false, true);
        }

        protected override void HandleTurnBegan(Unit u)
        {
            _active = u;
            RebuildStrip();
            RebuildTimeRow();
            RefreshImmediate();
            Show(true);
        }

        protected override void HandleTurnEnded(Unit u)
        {
            Show(false);
            _active = null;
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

        // ---------- 构建 ----------
        void RebuildStrip()
        {
            // 清空
            for (int i = stripRoot.childCount - 1; i >= 0; --i)
                Destroy(stripRoot.GetChild(i).gameObject);
            _segments.Clear();
            _typeSnapshot.Clear();            // ★ 关键修复：切人时先清空快照

            if (_active == null) return;

            // 1) 扫描该职业技能 → 资源类型集合（排除 HP/Energy）
            var types = CollectTypesFromSkills(_active.ClassId);

            // 2) 创建
            bool firstGroup = true;
            foreach (var t in types)
            {
                string key = ToStatsKey(t);
                int max = Mathf.Max(1, GetStatMaxInt(_active.Stats, key, GetDefaultMax(t)));
                int cur = Mathf.Clamp(GetStatInt(_active.Stats, key), 0, max);
                _typeSnapshot.Add((t, max, cur));

                if (!firstGroup && groupGap > 0.01f)
                {
                    var gap = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement));
                    gap.transform.SetParent(stripRoot, false);
                    gap.GetComponent<LayoutElement>().minWidth = groupGap;
                }
                firstGroup = false;

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
            for (int i = timeRoot.childCount - 1; i >= 0; --i)
                Destroy(timeRoot.GetChild(i).gameObject);
            _timePips = Array.Empty<Image>();
            _timeLastMax = -1; _timeLastRemain = -1;

            if (_active == null) return;
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

        // ---------- 刷新 ----------
        void RefreshImmediate()
        {
            if (_active?.Stats == null) return;

            // 能量文本：cur/max +regen/2s
            float cur = TryGetFloat(_active.Stats, "Energy");
            float max = Mathf.Max(1f, TryGetFloat(_active.Stats, "MaxEnergy", 100f));
            float regen = TryGetFloat(_active.Stats, "EnergyRegenPer2s", 0f);
            if (energySlider) energySlider.value = Mathf.Clamp01(cur / max);
            if (energyText) energyText.text = $"{Mathf.RoundToInt(cur)}/{Mathf.RoundToInt(max)}  +{regen:0}/2s";

            UpdateStrip(true);
            UpdateTime(true);
        }

        void RefreshStep()
        {
            if (_active?.Stats == null) return;

            float cur = TryGetFloat(_active.Stats, "Energy");
            float max = Mathf.Max(1f, TryGetFloat(_active.Stats, "MaxEnergy", 100f));
            float regen = TryGetFloat(_active.Stats, "EnergyRegenPer2s", 0f);

            if (energySlider) energySlider.value = Mathf.Lerp(energySlider.value, Mathf.Clamp01(cur / max), Time.deltaTime * energyLerp);
            if (energyText) energyText.text = $"{Mathf.RoundToInt(cur)}/{Mathf.RoundToInt(max)}  +{regen:0}/2s";

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

            int total = Mathf.Max(1, _active.TurnTime);
            if (total != _timeLastMax) RebuildTimeRow();
            UpdateTime(false);
        }

        void UpdateStrip(bool force)
        {
            if (_segments.Count == 0) return;

            var curByType = new Dictionary<CostResourceType, int>();
            foreach (var (type, max, _) in _typeSnapshot)
            {
                string key = ToStatsKey(type);
                int cur = Mathf.Clamp(GetStatInt(_active.Stats, key), 0, max);
                curByType[type] = cur;
            }

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
            if (timeText) timeText.text = remain.ToString();
        }

        // ---------- 工具 ----------
        List<CostResourceType> CollectTypesFromSkills(string classId)
        {
            var set = new HashSet<CostResourceType>();
            foreach (var t in ClassResourceCatalog.GetResourceTypes(classId))
            {
                if (t == CostResourceType.Custom || t == CostResourceType.HP || t == CostResourceType.Energy) continue;
                set.Add(t);
            }

            var skills = SkillDatabase.GetSkillsForClass(classId);
            if (skills != null)
            {
                foreach (var s in skills)
                {
                    if (s?.costs == null) continue;
                    foreach (var c in s.costs)
                    {
                        var t = c.resourceType;
                        if (t == CostResourceType.HP || t == CostResourceType.Energy || t == CostResourceType.Custom) continue;
                        set.Add(t);
                    }
                }
            }
            var list = new List<CostResourceType>(set);
            list.Sort((a, b) => ((int)a).CompareTo((int)b)); // 稳定顺序
            return list;
        }

        static string ToStatsKey(CostResourceType t)
        {
            string s = t.ToString();
            return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        static int GetDefaultMax(CostResourceType t) => t switch
        {
            CostResourceType.Rage => 100,
            CostResourceType.combo => 5,
            CostResourceType.point => 3,
            CostResourceType.qi => 4,
            CostResourceType.posture => 4,
            _ => 5
        };

        Color GetColor(CostResourceType t)
        {
            var o = paletteOverride.Find(p => p.type.Equals(t));
            if (o != null) return o.color;

            return t switch
            {
                CostResourceType.Discipline => new Color(0.18f, 0.6f, 1f),
                CostResourceType.Iron => new Color(0.6f, 0.6f, 0.65f),
                CostResourceType.Rage => new Color(1f, 0.2f, 0.2f),
                CostResourceType.Versatility => new Color(1f, 0.7f, 0.15f),
                CostResourceType.Gunpowder => new Color(0.55f, 0.4f, 0.2f),
                CostResourceType.point => new Color(1f, 0.35f, 0.35f),
                CostResourceType.combo => new Color(1f, 1f, 1f),
                CostResourceType.punch => new Color(1f, 0.5f, 0.1f),
                CostResourceType.qi => new Color(0.2f, 0.9f, 0.9f),
                CostResourceType.vision => new Color(0.7f, 0.5f, 1f),
                CostResourceType.posture => new Color(0.25f, 0.9f, 0.25f),
                _ => new Color(0.8f, 0.8f, 0.8f)
            };
        }

        // 反射：读 Stats
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
            string[] cand = { $"{key}Max", $"Max{key}", $"{key}_Max", $"max{key}", $"{FirstUpper(key)}Cap", $"{key}Cap" };
            foreach (var name in cand)
            {
                float v = TryGetFloat(stats, name, float.NaN);
                if (!float.IsNaN(v)) return Mathf.Max(1, Mathf.RoundToInt(v));
            }
            return fallback;
        }
        static string FirstUpper(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

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
