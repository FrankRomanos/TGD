using System.Collections.Generic;
using TGD.Grid;
using UnityEngine;

namespace TGD.Level
{
    /// <summary>
    /// Simple helper that displays a pool of ring prefabs on top of hex cells.
    /// </summary>
    public class HexRangeIndicator : MonoBehaviour
    {
        [Header("Grid")]
        public HexGridAuthoring grid;

        [Header("Visual")]
        public GameObject ringPrefab;
        public Transform container;
        public float hoverOffset = 0.05f;

        readonly List<Transform> _pool = new();
        float _cachedYaw;

        void Awake()
        {
            if (!grid)
                grid = GetComponentInParent<HexGridAuthoring>();
        }

        public void Show(IEnumerable<HexCoord> coordinates)
        {
            if (grid?.Layout == null || ringPrefab == null)
                return;

            var layout = grid.Layout;
            int index = 0;

            foreach (var coord in coordinates)
            {
                if (!layout.Contains(coord))
                    continue;

                var ring = GetOrCreate(index++);
                PositionRing(ring, coord);
            }

            HideFrom(index);
        }

        public void HideAll()
        {
            HideFrom(0);
        }

        Transform GetOrCreate(int index)
        {
            while (_pool.Count <= index)
            {
                var parent = container ? container : transform;
                var instance = Instantiate(ringPrefab, parent);
                instance.SetActive(false);
                _pool.Add(instance.transform);
            }

            var ring = _pool[index];
            if (!ring.gameObject.activeSelf)
                ring.gameObject.SetActive(true);
            return ring;
        }

        void HideFrom(int start)
        {
            for (int i = start; i < _pool.Count; i++)
            {
                var ring = _pool[i];
                if (ring && ring.gameObject.activeSelf)
                    ring.gameObject.SetActive(false);
            }
        }

        void PositionRing(Transform ring, HexCoord coord)
        {
            if (ring == null || grid?.Layout == null)
                return;

            var pos = grid.Layout.GetWorldPosition(coord, grid.tileHeightOffset + hoverOffset);
            if (!Mathf.Approximately(_cachedYaw, grid.Layout.YawDegrees))
                _cachedYaw = grid.Layout.YawDegrees;

            ring.SetPositionAndRotation(pos, Quaternion.Euler(0f, _cachedYaw, 0f));
        }
    }
}
