using System;
using TGD.CoreV2;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TGD.HexBoard
{
    /// <summary>
    /// Centralized service for converting between hex coordinates and world space.
    /// This acts as the single source of truth for any system that needs map/world positions.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class HexSpace : MonoBehaviour
    {
        static HexSpace _instance;
        static bool _searched;

        [Header("Board Binding")]
        [SerializeField] HexBoardAuthoringLite authoring;
        [SerializeField] float fallbackY = 0.01f;

        [Header("Debug")]
        [SerializeField] bool gizmoEnabled = true;
        [SerializeField] float gizmoRadius = 0.18f;
        [SerializeField] Color gizmoHitColor = new Color(0.2f, 0.8f, 1f, 0.8f);
        [SerializeField] Color gizmoSnapColor = new Color(1f, 0.85f, 0.3f, 0.9f);
        public bool hoverShowQrOnly = true;      // ← 只显示 (q,r)
        public bool hoverShowWorld = false;      // ← 是否附带世界坐标
        public float hoverLabelYOffset = 0.0f;   // 标签抬高一点更清楚
        [Header("Hover Gizmo")]
        public float hoverLabelOffsetRight = 0.6f;  // 往右偏多少
        public float hoverLabelOffsetUp = 0.8f;  // 往上偏多少

        public static HexSpace Instance
        {
            get
            {
                if (_instance == null && !_searched)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<HexSpace>(FindObjectsInactive.Include);
#else
            _instance = FindObjectOfType<HexSpace>();
#endif
                    _searched = true;
                }
                return _instance;
            }
            private set { _instance = value; _searched = (value != null); }
        }

        public HexBoardAuthoringLite Authoring
        {
            get => authoring;
            set
            {
                if (authoring == value) return;
                authoring = value;
                EnsureLayout();
            }
        }

        public float DefaultY
        {
            get
            {
                if (authoring != null)
                    return authoring.y;
                return fallbackY;
            }
        }

        public HexBoardLayout Layout
        {
            get
            {
                EnsureLayout();
                return authoring != null ? authoring.Layout : null;
            }
        }

        void Awake()
        {
            EnsureLayout();
        }

        void OnEnable()
        {
            Instance = this;
            EnsureLayout();
        }

        void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
                _searched = false;
            }
        }

        void EnsureLayout()
        {
            if (authoring != null && authoring.Layout == null)
                authoring.Rebuild();
        }

        public Vector3 HexToWorld(Hex hex, float? yOverride = null)
        {
            var layout = Layout;
            float y = yOverride ?? DefaultY;
            if (layout == null)
            {
                Debug.LogWarning($"[HexSpace] Layout missing while resolving {hex}. Using axial fallback.", this);
                return new Vector3(hex.q, y, hex.r);
            }
            return layout.World(hex, y);
        }

        public bool TryHexToWorld(Hex hex, out Vector3 world, float? yOverride = null)
        {
            var layout = Layout;
            if (layout == null)
            {
                world = default;
                return false;
            }
            world = layout.World(hex, yOverride ?? DefaultY);
            return true;
        }

        public Hex WorldToHex(Vector3 world)
        {
            var layout = Layout;
            if (layout == null)
            {
                return new Hex(Mathf.RoundToInt(world.x), Mathf.RoundToInt(world.z));
            }
            return layout.HexAt(world);
        }

        public Vector3 GetUnitWorldPosition(Unit unit, float? yOverride = null)
        {
            if (unit == null)
                throw new ArgumentNullException(nameof(unit));

            if (UnitLocator.TryGetTransform(unit.Id, out var transform) && transform)
                return transform.position;

            return HexToWorld(unit.Position, yOverride);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (!gizmoEnabled) return;
            var layout = Layout;
            if (layout == null) return;

            if (!TryGetMouseRay(out var ray)) return;
            if (!TryProjectToBoard(layout, ray, out var hit)) return;

            var hex = layout.HexAt(hit);
            var snapped = layout.World(hex, DefaultY);

            Gizmos.color = gizmoHitColor;
            Gizmos.DrawWireSphere(hit, gizmoRadius);
            Gizmos.color = gizmoSnapColor;
            Gizmos.DrawSphere(snapped, gizmoRadius * 0.6f);

            // ---- 只显示 q,r；可选是否附带 world ----
            string label = hoverShowQrOnly
                ? $"({hex.q},{hex.r})" + (hoverShowWorld ? $"  @ {snapped:F2}" : "")
                : $"{hex}" + (hoverShowWorld ? $"  @ {snapped:F2}" : "");
            // —— 按相机方向把标签挪远一些（缩放自适应）——
            var sv = SceneView.currentDrawingSceneView ?? SceneView.lastActiveSceneView;
            var cam = sv != null ? sv.camera : (Camera.current ?? Camera.main);
            Vector3 labelPos = snapped;
            if (cam != null)
            {
                float s = HandleUtility.GetHandleSize(snapped); // 跟随 Scene 缩放
                labelPos += cam.transform.right * (hoverLabelOffsetRight * s)
                          + cam.transform.up * (hoverLabelOffsetUp * s);
            }

            Handles.color = gizmoSnapColor;
            Handles.Label(snapped + Vector3.up * hoverLabelYOffset, label);
        }
        // ✅ 改成用 HandleUtility，保证“鼠标指哪就哪”
        bool TryGetMouseRay(out Ray ray)
        {
            // 在 Scene 视图内：最稳妥的方案
            if (Event.current != null)
            {
                ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
                return true;
            }

            // 兜底：用当前绘制的 SceneView 相机，或主相机（Game 视图）
            var sv = SceneView.currentDrawingSceneView ?? SceneView.lastActiveSceneView;
            var cam = sv != null ? sv.camera : (Camera.current ?? Camera.main);
            if (cam != null)
            {
                ray = cam.ScreenPointToRay(Input.mousePosition);
                return true;
            }

            ray = default;
            return false;
        }


        bool TryProjectToBoard(HexBoardLayout layout, Ray ray, out Vector3 hit)
        {
            float planeY = layout.origin.y + DefaultY;
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            if (plane.Raycast(ray, out var distance))
            {
                hit = ray.GetPoint(distance);
                return true;
            }

            hit = default;
            return false;
        }
        // HexSpace.cs 里增加：
        public static string Explain(Hex h)
        {
            var inst = Instance;
            if (inst == null || inst.Layout == null) return "[HexSpace] No layout.";
            var w = inst.Layout.World(h, inst.DefaultY);
            var back = inst.Layout.HexAt(w);
            return $"{h} -> {w} -> {back}";
        }

        [ContextMenu("Self Test (0,0) (8,6) (11,5)")]
        void SelfTest()
        {
            Debug.Log(Explain(new Hex(0, 0)), this);
            Debug.Log(Explain(new Hex(8, 6)), this);
            Debug.Log(Explain(new Hex(11, 5)), this);
        }

        // 可选：对某个 unit 做“变换 vs 逻辑”的对照打印
        public static void DebugUnit(Unit u)
        {
            if (u == null) { Debug.Log("[HexSpace] DebugUnit: null"); return; }
            var hex = u.Position;
            var mapped = Instance.HexToWorld(hex);
            if (UnitLocator.TryGetTransform(u.Id, out var t) && t)
                Debug.Log($"[Unit] {u.Id} hex={hex}  view={t.position}  map={mapped}");
            else
                Debug.Log($"[Unit] {u.Id} hex={hex}  (no view)  map={mapped}");
        }

#endif
    }
}
