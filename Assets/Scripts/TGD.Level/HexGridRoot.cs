using UnityEngine;
using TGD.Grid;

namespace TGD.Level
{
    public class HexGridAuthoring : MonoBehaviour
    {
        [Header("Layout")]
        public int width = 12;
        public int height = 12;
        public float radius = 1.0f;                 // ��� HexTiles Ԥ�ơ�ƽ�̵�ƽ�����ġ��İ뾶
        public HexOrientation orientation = HexOrientation.FlatTop;
        public HexOffsetMode offsetMode = HexOffsetMode.OddRow;
        public Transform origin;                    // ��ѡ������ԭ��
        public float tileHeightOffset = 0f;

        public HexGridLayout Layout { get; private set; }

        void Awake()
        {
            var originPos = origin ? origin.position : Vector3.zero;
            Layout = new HexGridLayout(width, height, radius, orientation, offsetMode, originPos);
        }
    }
}
