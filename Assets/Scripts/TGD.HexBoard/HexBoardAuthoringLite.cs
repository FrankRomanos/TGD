using UnityEngine;

namespace TGD.HexBoard
{
    /// <summary>
    /// ���� Authoring���� Inspector ���������̳ߴ�/��λ����/ԭ��/����Flat/Pointy����
    /// </summary>
    [ExecuteAlways]
    public sealed class HexBoardAuthoringLite : MonoBehaviour
    {
        [Header("Board Size (axial q,r)")]
        public int width = 40; public int height = 30;
        public int minQ = 0; public int minR = 0;

        [Header("World Mapping")]
        [Tooltip("����->���� �ľ��루�뾶��")] public float cellSize = 1f;
        public Transform origin; public float y = 0.01f;
        public HexOrient orient = HexOrient.FlatTop;

        public HexBoardLayout Layout { get; private set; }

        void OnEnable() => Rebuild();
        void OnValidate() => Rebuild();

        public void Rebuild()
        {
            var org = origin ? origin.position : Vector3.zero;
            Layout = new HexBoardLayout(width, height, cellSize, org, minQ, minR, orient);
        }
    }
}
