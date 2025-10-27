using System;
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
            if (!gizmoEnabled)
                return;
            var layout = Layout;
            if (layout == null)
                return;

            if (!TryGetMouseRay(out var ray))
                return;
            if (!TryProjectToBoard(layout, ray, out var hit))
                return;

            var hex = layout.HexAt(hit);
            var snapped = layout.World(hex, DefaultY);

            Gizmos.color = gizmoHitColor;
            Gizmos.DrawWireSphere(hit, gizmoRadius);
            Gizmos.color = gizmoSnapColor;
            Gizmos.DrawSphere(snapped, gizmoRadius * 0.6f);

            Handles.color = gizmoSnapColor;
            Handles.Label(snapped, $"{hex} @ {snapped:F2}");
        }

        bool TryGetMouseRay(out Ray ray)
        {
            if (Application.isPlaying)
            {
                var cam = Camera.main ?? Camera.current;
                if (cam != null)
                {
                    ray = cam.ScreenPointToRay(Input.mousePosition);
                    return true;
                }
            }

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.camera != null)
            {
                var cam = sceneView.camera;
                var evt = Event.current;
                Vector2 mousePos = evt != null ? evt.mousePosition : new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
                mousePos.y = cam.pixelHeight - mousePos.y;
                ray = cam.ScreenPointToRay(mousePos);
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
#endif
    }
}
