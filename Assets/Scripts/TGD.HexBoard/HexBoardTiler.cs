using UnityEngine;
using System.Collections.Generic;

namespace TGD.HexBoard
{
    /// <summary>
    /// 把整个棋盘用一个 hex prefab 铺开（蜂窝排布）。
    /// </summary>
    [ExecuteAlways]
    public sealed class HexBoardTiler : MonoBehaviour
    {
        // HexBoardTiler.cs —— 放在类里（字段）
        [SerializeField] bool autoClearOnPrefabChange = true;
        GameObject _lastPrefab;

        // HexBoardTiler.cs —— 放在类里（方法）
        [ContextMenu("Rebuild Now")]
        public void RebuildNow() => Rebuild();

        [ContextMenu("Clear All Tiles")]
        public void ClearAll() => Clear();

        void OnValidate()
        {
            if (!autoRebuild) return;

            // 侦测 prefab 是否变化
            if (_lastPrefab != hexPrefab)
            {
                if (autoClearOnPrefabChange)
                    Clear();                                  // ★ 自动清掉旧的
                _lastPrefab = hexPrefab;
            }

            if (hexPrefab == null) return;                    // 无 prefab 就不重建
            Rebuild();
        }

        public HexBoardAuthoringLite authoring;
        public GameObject hexPrefab;            // 你的六边形资产
        public float prefabSizeInWorld = 1f;    // 该 prefab 的“中心->顶点”世界距离（看模型尺寸），用于缩放到 cellSize
        public Vector3 prefabLocalEuler = Vector3.zero;
        public bool autoRebuild = true;

        readonly Dictionary<Hex, GameObject> tiles = new();
        public IReadOnlyDictionary<Hex, GameObject> Tiles => tiles;

        void OnEnable() { if (autoRebuild) Rebuild(); }
  

        public void Rebuild()
        {
            Clear();
            if (authoring == null || authoring.Layout == null || hexPrefab == null) return;
            var L = authoring.Layout;
            float k = (prefabSizeInWorld <= 1e-6f) ? 1f : (L.cellSize / prefabSizeInWorld);

            foreach (var h in L.Coordinates())
            {
                var pos = L.World(h, authoring.y);
                var go = Instantiate(hexPrefab, pos, Quaternion.Euler(prefabLocalEuler), this.transform);
                go.name = $"Hex {h.q},{h.r}";
                go.transform.localScale = go.transform.localScale * k;
                tiles[h] = go;
            }
        }

        public bool TryGetTile(Hex h, out GameObject go) => tiles.TryGetValue(h, out go);

        public void Clear()
        {
            foreach (var kv in tiles)
            {
                if (Application.isPlaying) Destroy(kv.Value); else DestroyImmediate(kv.Value);
            }
            tiles.Clear();
        }
    }
}

