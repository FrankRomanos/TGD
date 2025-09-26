using UnityEngine;
using System.Collections.Generic;

namespace TGD.HexBoard
{
    /// <summary>
    /// ������������һ�� hex prefab �̿��������Ų�����
    /// </summary>
    [ExecuteAlways]
    public sealed class HexBoardTiler : MonoBehaviour
    {
        // HexBoardTiler.cs ���� ��������ֶΣ�
        [SerializeField] bool autoClearOnPrefabChange = true;
        GameObject _lastPrefab;

        // HexBoardTiler.cs ���� �������������
        [ContextMenu("Rebuild Now")]
        public void RebuildNow() => Rebuild();

        [ContextMenu("Clear All Tiles")]
        public void ClearAll() => Clear();

        void OnValidate()
        {
            if (!autoRebuild) return;

            // ��� prefab �Ƿ�仯
            if (_lastPrefab != hexPrefab)
            {
                if (autoClearOnPrefabChange)
                    Clear();                                  // �� �Զ�����ɵ�
                _lastPrefab = hexPrefab;
            }

            if (hexPrefab == null) return;                    // �� prefab �Ͳ��ؽ�
            Rebuild();
        }

        public HexBoardAuthoringLite authoring;
        public GameObject hexPrefab;            // ����������ʲ�
        public float prefabSizeInWorld = 1f;    // �� prefab �ġ�����->���㡱������루��ģ�ͳߴ磩���������ŵ� cellSize
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

