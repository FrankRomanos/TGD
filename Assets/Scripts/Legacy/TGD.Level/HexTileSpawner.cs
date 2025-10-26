using System;
using UnityEngine;
using TGD.Grid; // 你的网格命名空间

namespace TGD.Level
{
    [Serializable]
    public struct WeightedPrefab
    {
        public GameObject prefab;
        [Range(0f, 1f)] public float weight;
    }

    /// <summary>
    /// 按 HexGridAuthoring 的网格坐标铺石块（支持与 Plane 对齐、统一角度、可选60°随机）
    /// 支持编辑器下生成并保存（配合自定义Inspector按钮或ContextMenu）
    /// </summary>
    [ExecuteAlways]
    public class HexTileSpawner : MonoBehaviour
    {
        [Header("Grid")]
        public HexGridAuthoring grid;   // 拖 HexGridRoot（里面有 HexGridAuthoring）
        public Transform parent;        // 生成到哪个节点下（建议建一个 Tiles 空物体）

        [Header("Rotation")]
        public bool alignToOriginYaw = true;                 // 与 origin(=Plane) 的 Y 朝向对齐
        [Range(-180f, 180f)] public float yRotationOffset = 0f; // 统一偏移（常用 ±30°）
        public bool randomRotate60 = false;                  // 在统一朝向的基础上再做 60° 倍数随机

        [Header("Palette (stones only)")]
        public WeightedPrefab[] stones;                      // 拖 PF_HexBoard_StoneVar0/1/2/Chiseled_1 等
        [Range(0, 999999)] public int randomSeed = 12345;
        public bool clearExisting = true;                    // 生成前清空 parent 下旧砖

        System.Random rng;

        void OnValidate()
        {
            if (!parent) parent = transform;
        }

        [ContextMenu("Generate Now")]
        public void GenerateNow()
        {
            // 确保网格可用（编辑器下也能重建）
            if (!grid)
            {
                Debug.LogWarning("[HexTileSpawner] 请先在场景中放好 HexGridAuthoring 并拖到 grid 字段。");
                return;
            }
            if (grid.Layout == null)
            {
                // 若你的 HexGridAuthoring 有 Rebuild()（见下方备注），这里可直接重建
                try { grid.Rebuild(); } catch { }
                if (grid.Layout == null)
                {
                    Debug.LogWarning("[HexTileSpawner] grid.Layout 为空，请检查 HexGridAuthoring 是否已初始化。");
                    return;
                }
            }

            if (!parent) parent = transform;

            // 清空旧砖
            if (clearExisting)
            {
                for (int i = parent.childCount - 1; i >= 0; --i)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying) DestroyImmediate(parent.GetChild(i).gameObject);
                    else Destroy(parent.GetChild(i).gameObject);
#else
                    Destroy(parent.GetChild(i).gameObject);
#endif
                }
            }

            // 权重校验
            float total = 0f;
            if (stones != null)
                foreach (var w in stones) total += Mathf.Max(0, w.weight);
            if (stones == null || stones.Length == 0 || total <= 0f)
            {
                Debug.LogWarning("[HexTileSpawner] 请在 stones 里拖入至少一个预制且权重>0");
                return;
            }

            rng = new System.Random(randomSeed);

            // 计算基准朝向
            float baseYaw = 0f;
            if (alignToOriginYaw && grid.origin) baseYaw = grid.origin.eulerAngles.y;

            int count = 0;
            foreach (var c in grid.Layout.Coordinates)
            {
                var pos = grid.Layout.GetWorldPosition(c, grid.tileHeightOffset);

                // 选一个预制
                var prefab = PickByWeight(stones, total);
                if (!prefab) continue;

                // 统一朝向 + 可选60°随机
                float randYaw = randomRotate60 ? 60f * rng.Next(0, 6) : 0f;
                var rot = Quaternion.Euler(0f, baseYaw + yRotationOffset + randYaw, 0f);

                var go = Instantiate(prefab, pos, rot, parent);
                go.name = $"Stone_{c.Q}_{c.R}";

#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEditor.Undo.RegisterCreatedObjectUndo(go, "HexTiles Generate");
#endif

                count++;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif

            Debug.Log($"[HexTileSpawner] 生成 {count} 块石砖（seed={randomSeed}）");
        }

        [ContextMenu("Clear")]
        public void ClearNow()
        {
            if (!parent) parent = transform;
            for (int i = parent.childCount - 1; i >= 0; --i)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(parent.GetChild(i).gameObject);
                else Destroy(parent.GetChild(i).gameObject);
#else
                Destroy(parent.GetChild(i).gameObject);
#endif
            }
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }

        GameObject PickByWeight(WeightedPrefab[] arr, float total)
        {
            float t = (float)rng.NextDouble() * total;
            foreach (var w in arr)
            {
                float ww = Mathf.Max(0, w.weight);
                if (t <= ww) return w.prefab;
                t -= ww;
            }
            return arr[arr.Length - 1].prefab;
        }
    }
}
