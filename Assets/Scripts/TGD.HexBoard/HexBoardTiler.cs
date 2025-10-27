using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TGD.HexBoard
{
    public sealed class HexTileMarker : MonoBehaviour
    {
        public int q, r;
        public Hex Coord => new Hex(q, r);
        public void Set(Hex h) { q = h.q; r = h.r; }
    }

    /// 最稳妥：不在编辑器里自动生成；只在运行时或手动按钮重建。
    public sealed class HexBoardTiler : MonoBehaviour
    {
        [Header("Refs")]
        public HexBoardAuthoringLite authoring;

        [Header("Tile Prefab & Build")]
        public GameObject tilePrefab;
        public Transform parent;              // 建议一个空物体“Tiles”
        public float y = 0.01f;
        public bool stripColliders = true;

        [Header("Prefab Fitting")]
        public float prefabSizeInUnits = 1f;  // 你的预制体“单位尺寸”
        public float scaleMultiplier = 1f;    // 乘到 cellSize 上
        public Vector3 prefabLocalEuler = new Vector3(90, 0, 0);
        public Vector3 prefabLocalOffset = Vector3.zero;

        [Header("Runtime")]
        public bool buildOnStart = true;      // 需要网格时勾上；不需要可关

        public readonly Dictionary<Hex, GameObject> Tiles = new();
        bool _built;
        static bool _purgedOnceThisPlay;      // 每次进入 Play 只全局清一次

        void Reset()
        {
            if (!authoring) authoring = GetComponent<HexBoardAuthoringLite>();
        }

        void Awake()
        {
            // ★ 进入 Play：先把场景里任何历史砖一网打尽
            if (Application.isPlaying && !_purgedOnceThisPlay)
            {
                PurgeAllTilesInScene();
                _purgedOnceThisPlay = true;
            }
        }

        void Start()
        {
            if (!authoring) authoring = GetComponent<HexBoardAuthoringLite>();
            if (Application.isPlaying && buildOnStart)
                Rebuild();
        }

        public void EnsureBuilt()
        {
            if (_built) return;
            Rebuild();
        }

        [ContextMenu("Rebuild (Runtime/Editor)")]
        public void Rebuild()
        {
            Clear(includeOrphans: false); // 先清自己这批

            if (authoring == null || authoring.Layout == null || tilePrefab == null) return;

            var L = authoring.Layout;
            float target = Mathf.Max(1e-4f, authoring.cellSize * Mathf.Max(0.0001f, scaleMultiplier));
            float k = target / Mathf.Max(1e-4f, prefabSizeInUnits);

            Transform p = parent ? parent : transform;

            var space = HexSpace.Instance;
            if (space == null)
            {
                Debug.LogWarning("[HexBoardTiler] HexSpace instance is missing; aborting tile rebuild.", this);
                return;
            }

            foreach (var h in L.Coordinates())
            {
                Vector3 pos = space.HexToWorld(h, y) + prefabLocalOffset;
                var go = Instantiate(tilePrefab, pos, Quaternion.Euler(prefabLocalEuler), p);
                go.name = $"Tile [{h.q},{h.r}]";
                go.transform.localScale = go.transform.localScale * k;

                if (stripColliders)
                {
                    var cols = go.GetComponentsInChildren<Collider>(true);
                    for (int i = 0; i < cols.Length; i++)
#if UNITY_EDITOR
                        if (!Application.isPlaying) DestroyImmediate(cols[i]);
                        else
#endif
                            Destroy(cols[i]);
                }

                var mark = go.GetComponent<HexTileMarker>() ?? go.AddComponent<HexTileMarker>();
                mark.Set(h);
                Tiles[h] = go;
            }

            _built = true;
        }

        [ContextMenu("Clear (Only Mine)")]
        public void ClearOnlyMine() => Clear(includeOrphans: false);

        [ContextMenu("Purge All Tiles In Scene")]
        public void PurgeAllInSceneButton() => PurgeAllTilesInScene();

        public void Clear(bool includeOrphans)
        {
            // 先清掉这次 Tiler 生成并记录的
            foreach (var kv in Tiles)
            {
                var go = kv.Value;
                if (!go) continue;
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(go);
                else
#endif
                    Destroy(go);
            }
            Tiles.Clear();
            _built = false;

            // 可选：把场景里任何“孤儿砖”也清理掉（不管父节点在哪）
            if (includeOrphans)
                PurgeAllTilesInScene();
        }

        /// ★ 全场景核清：把任何 HexTileMarker 都删掉（含隐藏/未激活），
        ///    另外兜底删除名字看起来像旧砖的对象。
        public static void PurgeAllTilesInScene()
        {
            // 1) 带 Marker 的最可靠
            foreach (var m in FindAll<HexTileMarker>())
            {
                var go = m ? m.gameObject : null;
                if (!go) continue;
#if UNITY_EDITOR
                if (!Application.isPlaying) Object.DestroyImmediate(go);
                else
#endif
                    Object.Destroy(go);
            }

            // 2) 兜底：老版本可能没有 Marker —— 用名字+组件特征清理
            foreach (var go in FindAll<GameObject>())
            {
                if (!go) continue;
                if (!go.scene.IsValid()) continue;        // 跳过资源/Prefab 资产
                var name = go.name;
                bool looksLikeTile = (name.StartsWith("Tile [") || name.StartsWith("Hex "))
                                     && go.GetComponent<MeshRenderer>() != null;
                if (!looksLikeTile) continue;

#if UNITY_EDITOR
                if (!Application.isPlaying) Object.DestroyImmediate(go);
                else
#endif
                    Object.Destroy(go);
            }
        }

        // —— 工具：同时适配编辑器 & 运行时的“找所有对象” ——
        static T[] FindAll<T>() where T : Object
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return Resources.FindObjectsOfTypeAll<T>();
#endif
#if UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<T>(true);
#endif
        }

        public bool TryGetTile(Hex h, out GameObject go) => Tiles.TryGetValue(h, out go) && go;
    }
}