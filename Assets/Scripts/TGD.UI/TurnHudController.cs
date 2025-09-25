using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;                 // �� asmdef δ���� TextMeshPro���ɰ� TMP_Text ���� UnityEngine.UI.Text
using UnityEngine;
using UnityEngine.UI;
using TGD.Combat;
using TGD.Data;             // �õ� SkillDatabase��CostResourceType

namespace TGD.UI
{
    /// <summary>
    /// �ײ� HUD�����ֵ�λר�ã���
    /// - ����ͨ����������Energy��
    /// - �£�ְҵ��Դ�����ɲ�ͬ��ɫ��С����ƴ�ӣ��Զ��Ӹ�ְҵ������ɨ�����Դ���ͣ��ų� HP/Energy��
    /// - ��ѡ���غ�ʱ�䶹��ά����֮ǰ��С���߼���
    /// </summary>
    public class TurnHudController : MonoBehaviour
    {
        [Header("Refs")]
        public CombatLoop combat;                    // �ɿգ��Զ���
        public CanvasGroup canvasGroup;              // ��������

        [Header("Energy Bar")]
        public Slider energySlider;
        public TMP_Text energyText;                  // ����ʾ��ǰֵ���� cur/max ���иģ�
        [Range(1f, 20f)] public float energyLerp = 10f;

        [Header("Class Resource Strip (colored segments)")]
        public Transform stripRoot;                  // ˮƽ����
        public GameObject stripCellPrefab;           // С����Ԥ�ƣ�Image������ 18x8��
        [Range(0f, 1f)] public float offAlpha = 0.25f;
        public float groupGap = 8f;                  // ��ͬ��Դ����֮��Ŀ�϶��Layout ��ʵ�֣�

        [Header("Turn Time (optional)")]
        public bool showTimeBeans = true;
        public Transform timeRoot;                   // ˮƽ����
        public GameObject timePipPrefab;             // С��Ԥ�ƣ�Image������ 14x14��
        public Color timeTint = Color.white;
        [Range(0f, 1f)] public float timeOffAlpha = 0.25f;
        public TMP_Text timeText;

        [Header("Update")]
        public float updateInterval = 0.1f;          // ��ѯˢ�£�û���¼����߾�������

        // ���� ����̬ ���� //
        Unit _active;
        float _timer;

        struct Seg
        {
            public Image img;
            public Color color;
            public CostResourceType type;
            public int indexInType; // ͬ���͵ĵڼ��������ڵ���ǰ N ��
        }
        readonly List<Seg> _segments = new();        // ����ʾ˳�������С����
        readonly List<(CostResourceType type, int max, int cur)> _typeSnapshot = new();

        Image[] _timePips = Array.Empty<Image>();
        int _timeLastMax = -1, _timeLastRemain = -1;

        // ���� ��ѡ����ɫ�Զ��壨Inspector ���ǣ����� //
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

        // ----------------- ���� UI -----------------

        void RebuildStrip()
        {
            // ���
            for (int i = stripRoot.childCount - 1; i >= 0; --i)
                Destroy(stripRoot.GetChild(i).gameObject);
            _segments.Clear();

            if (_active == null) return;

            // 1) ɨ���ְҵ���� �� ��Դ���ͼ��ϣ��ų� HP/Energy��
            var types = CollectTypesFromSkills(_active.ClassId);

            // 2) ����Դ�������δ���С����
            bool firstGroup = true;
            foreach (var t in types)
            {
                // ȡ��ǰֵ/���ޣ��� Stats �ﷴ�䣻���������ֶΣ���Ĭ�ϣ�
                var key = ToStatsKey(t);
                int max = Mathf.Max(1, GetStatMaxInt(_active.Stats, key, GetDefaultMax(t)));
                int cur = Mathf.Clamp(GetStatInt(_active.Stats, key), 0, max);
                _typeSnapshot.Add((t, max, cur));

                // ����϶������һ���� GameObject���� LayoutElement minWidth = groupGap��
                if (!firstGroup && groupGap > 0.01f)
                {
                    var gapGO = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement));
                    gapGO.transform.SetParent(stripRoot, false);
                    gapGO.GetComponent<LayoutElement>().minWidth = groupGap;
                }
                firstGroup = false;

                // ���� max �� segment
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
            // ���
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

        // ----------------- ˢ�� -----------------

        void RefreshImmediate()
        {
            if (_active?.Stats == null) return;

            // ����
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

            // ������ƽ����
            float cur = TryGetFloat(_active.Stats, "Energy");
            float max = Mathf.Max(1f, TryGetFloat(_active.Stats, "MaxEnergy", 100f));
            if (energySlider) energySlider.value = Mathf.Lerp(energySlider.value, Mathf.Clamp01(cur / max), Time.deltaTime * energyLerp);
            if (energyText) energyText.text = $"{Mathf.RoundToInt(cur)}";

            // ��Դ������ĳ��Դ���ޱ�������� BUFF������Ҫ�ؽ�
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

            // ʱ�䶹��TurnTime �ı� �� �ؽ�
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

            // ����ÿ�����͵�ǰֵ
            var curByType = new Dictionary<CostResourceType, int>();
            foreach (var (type, max, _) in _typeSnapshot)
            {
                var key = ToStatsKey(type);
                int cur = Mathf.Clamp(GetStatInt(_active.Stats, key), 0, max);
                curByType[type] = cur;
            }

            // ����ǰ N ��
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

        // ----------------- ���ߣ�ɨ��/����/��ɫ -----------------

        // ɨ���ְҵ���ܣ�����ȥ�غ����Դ�����б��ų� HP/Energy��
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
            // �̶�һ���ȶ�˳��ö�ٶ���˳��
            list.Sort((a, b) => ((int)a).CompareTo((int)b));
            return list;
        }

        // ��ö��ת�� Stats �ֶ������ٶ���� Stats ��Ա����Щ��һ�£��� Rage/Combo/Qi/Versatility �ȣ�
        static string ToStatsKey(CostResourceType t)
        {
            // ֱ����ö����������ĸ��д���ɣ����� PascalCase ��ֱ�ӷ��أ�
            string s = t.ToString();      // Rage / Versatility / Gunpowder / combo / qi ...
            if (s.Length == 0) return s;
            // ͳһ������ĸ��д������ά��ԭ��
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        // Ĭ�����ޣ��� Stats ���Ҳ��� *Max ʱ��
        static int GetDefaultMax(CostResourceType t) => t switch
        {
            CostResourceType.Rage => 100,
            CostResourceType.combo => 5,
            CostResourceType.point => 3,
            CostResourceType.qi or CostResourceType.qi => 4,
            CostResourceType.posture => 4,
            _ => 5
        };

        // ��ɫ������ Inspector �� paletteOverride ����
        Color GetColor(CostResourceType t)
        {
            var o = paletteOverride.Find(p => p.type.Equals(t));
            if (o != null) return o.color;

            return t switch
            {
                CostResourceType.Discipline => new Color(0.18f, 0.6f, 1f),   // ����
                CostResourceType.Iron => new Color(0.6f, 0.6f, 0.65f), // ����
                CostResourceType.Rage => new Color(1f, 0.2f, 0.2f),    // ��
                CostResourceType.Versatility => new Color(1f, 0.7f, 0.15f),   // ���
                CostResourceType.Gunpowder => new Color(0.55f, 0.4f, 0.2f), // ��
                CostResourceType.point => new Color(1f, 0.35f, 0.35f),  // ���
                CostResourceType.combo => new Color(1f, 1f, 1f),        // ��
                CostResourceType.punch => new Color(1f, 0.5f, 0.1f),    // ��
                CostResourceType.qi => new Color(0.2f, 0.9f, 0.9f),   // ��
                CostResourceType.vision => new Color(0.7f, 0.5f, 1f),    // ��
                CostResourceType.posture => new Color(0.25f, 0.9f, 0.25f),// ��
                _ => new Color(0.8f, 0.8f, 0.8f)
            };
        }

        // ���䣺�� Stats �ϵ�ǰֵ / ����
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

        // 2023+ ����
        static T FindFirstObjectByTypeSafe<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<T>();
#endif
        }

        // ����
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
