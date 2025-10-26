using System;
using UnityEngine;
using TGD.Grid; // ������������ռ�

namespace TGD.Level
{
    [Serializable]
    public struct WeightedPrefab
    {
        public GameObject prefab;
        [Range(0f, 1f)] public float weight;
    }

    /// <summary>
    /// �� HexGridAuthoring ������������ʯ�飨֧���� Plane ���롢ͳһ�Ƕȡ���ѡ60�������
    /// ֧�ֱ༭�������ɲ����棨����Զ���Inspector��ť��ContextMenu��
    /// </summary>
    [ExecuteAlways]
    public class HexTileSpawner : MonoBehaviour
    {
        [Header("Grid")]
        public HexGridAuthoring grid;   // �� HexGridRoot�������� HexGridAuthoring��
        public Transform parent;        // ���ɵ��ĸ��ڵ��£����齨һ�� Tiles �����壩

        [Header("Rotation")]
        public bool alignToOriginYaw = true;                 // �� origin(=Plane) �� Y �������
        [Range(-180f, 180f)] public float yRotationOffset = 0f; // ͳһƫ�ƣ����� ��30�㣩
        public bool randomRotate60 = false;                  // ��ͳһ����Ļ��������� 60�� �������

        [Header("Palette (stones only)")]
        public WeightedPrefab[] stones;                      // �� PF_HexBoard_StoneVar0/1/2/Chiseled_1 ��
        [Range(0, 999999)] public int randomSeed = 12345;
        public bool clearExisting = true;                    // ����ǰ��� parent �¾�ש

        System.Random rng;

        void OnValidate()
        {
            if (!parent) parent = transform;
        }

        [ContextMenu("Generate Now")]
        public void GenerateNow()
        {
            // ȷ��������ã��༭����Ҳ���ؽ���
            if (!grid)
            {
                Debug.LogWarning("[HexTileSpawner] �����ڳ����зź� HexGridAuthoring ���ϵ� grid �ֶΡ�");
                return;
            }
            if (grid.Layout == null)
            {
                // ����� HexGridAuthoring �� Rebuild()�����·���ע���������ֱ���ؽ�
                try { grid.Rebuild(); } catch { }
                if (grid.Layout == null)
                {
                    Debug.LogWarning("[HexTileSpawner] grid.Layout Ϊ�գ����� HexGridAuthoring �Ƿ��ѳ�ʼ����");
                    return;
                }
            }

            if (!parent) parent = transform;

            // ��վ�ש
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

            // Ȩ��У��
            float total = 0f;
            if (stones != null)
                foreach (var w in stones) total += Mathf.Max(0, w.weight);
            if (stones == null || stones.Length == 0 || total <= 0f)
            {
                Debug.LogWarning("[HexTileSpawner] ���� stones ����������һ��Ԥ����Ȩ��>0");
                return;
            }

            rng = new System.Random(randomSeed);

            // �����׼����
            float baseYaw = 0f;
            if (alignToOriginYaw && grid.origin) baseYaw = grid.origin.eulerAngles.y;

            int count = 0;
            foreach (var c in grid.Layout.Coordinates)
            {
                var pos = grid.Layout.GetWorldPosition(c, grid.tileHeightOffset);

                // ѡһ��Ԥ��
                var prefab = PickByWeight(stones, total);
                if (!prefab) continue;

                // ͳһ���� + ��ѡ60�����
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

            Debug.Log($"[HexTileSpawner] ���� {count} ��ʯש��seed={randomSeed}��");
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
