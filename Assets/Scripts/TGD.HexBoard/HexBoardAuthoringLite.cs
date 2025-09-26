using UnityEngine;

namespace TGD.HexBoard
{
    /// <summary>
    /// 极简 Authoring：在 Inspector 里配置棋盘尺寸/单位长度/原点/朝向（Flat/Pointy）。
    /// </summary>
    [ExecuteAlways]
    public sealed class HexBoardAuthoringLite : MonoBehaviour
    {
        [Header("Board Size (axial q,r)")]
        public int width = 40; public int height = 30;
        public int minQ = 0; public int minR = 0;

        [Header("World Mapping")]
        [Tooltip("中心->顶点 的距离（半径）")] public float cellSize = 1f;
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
