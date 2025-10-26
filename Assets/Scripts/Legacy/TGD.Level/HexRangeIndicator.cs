using System.Collections.Generic;
using TGD.Grid;
using UnityEngine;
using static Codice.Client.Commands.WkTree.WorkspaceTreeNode;

namespace TGD.Level
{
    /// <summary>Displays a pool of ring prefabs on top of hex cells.</summary>
    public class HexRangeIndicator : MonoBehaviour
    {
        [Header("Grid")]
        public HexGridAuthoring grid;

        [Header("Visual")]
        public GameObject ringPrefab;
        public Transform container;
        public float hoverOffset = 0.2f;

        [Header("Sizing")]
        public bool fitToGridRadius = true;
        [Range(0.25f, 4f)] public float scaleMul = 1f;

        readonly List<Transform> _pool = new();
        float _cachedYaw;

        void Awake()
        {
            if (!grid) grid = GetComponentInParent<HexGridAuthoring>();
        }

        public void Show(IEnumerable<HexCoord> coordinates)
        {
            if (grid?.Layout == null || ringPrefab == null) return;

            var layout = grid.Layout;
            int index = 0;

            foreach (var coord in coordinates)
            {
                if (!layout.Contains(coord)) continue;

                var ring = GetOrCreate(index++);
                PositionRing(ring, coord);
            }

            HideFrom(index);
        }

        public void HideAll() => HideFrom(0);

        Transform GetOrCreate(int index)
        {
            while (_pool.Count <= index)
            {
                var parent = container ? container : transform;
                var go = Instantiate(ringPrefab, parent);
                go.SetActive(false);

                // 禁用所有碰撞，避免挡鼠标
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;

                // 首次实例化时按网格半径适配
                if (fitToGridRadius) FitToRadiusIfNeeded(go.transform);

                _pool.Add(go.transform);
            }

            var ring = _pool[index];
            if (!ring.gameObject.activeSelf) ring.gameObject.SetActive(true);
            return ring;
        }

        void HideFrom(int start)
        {
            for (int i = start; i < _pool.Count; i++)
            {
                var ring = _pool[i];
                if (ring && ring.gameObject.activeSelf) ring.gameObject.SetActive(false);
            }
        }

        void PositionRing(Transform ring, HexCoord coord)
        {
            if (ring == null || grid?.Layout == null) return;

            var pos = grid.Layout.GetWorldPosition(coord, grid.tileHeightOffset + hoverOffset);
            if (!Mathf.Approximately(_cachedYaw, grid.Layout.YawDegrees))
                _cachedYaw = grid.Layout.YawDegrees;

            var rot = Quaternion.AngleAxis(_cachedYaw, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            ring.SetPositionAndRotation(pos, rot);
        }

        void FitToRadiusIfNeeded(Transform ring)
        {
            if (!ring || grid?.Layout == null) return;

            // 临时激活拿 bounds
            bool wasOn = ring.gameObject.activeSelf;
            if (!wasOn) ring.gameObject.SetActive(true);

            var rend = ring.GetComponentInChildren<Renderer>(true);
            if (rend)
            {
                float worldWidth = rend.bounds.size.x;              // 预制体当前世界宽
                float target = 2f * grid.Layout.HexRadius;             // Flat-Top：横向直径 = 2r
                if (worldWidth > 1e-4f)
                {
                    float s = (target / worldWidth) * Mathf.Max(0.0001f, scaleMul);
                    ring.localScale *= s;
                }
            }

            if (!wasOn) ring.gameObject.SetActive(false);
        }
    }
}
