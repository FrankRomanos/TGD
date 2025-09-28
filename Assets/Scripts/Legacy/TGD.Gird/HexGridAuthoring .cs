using System;
using UnityEngine;


namespace TGD.Grid
{
    /// <summary>
    /// Authoring for HexGridLayout. Supports global yaw:
    /// - useOriginYaw: follow the Plane's Y rotation
    /// - yawOffset: extra tweak (Flat-Top ģ�ͳ�����Ҫ +30�� �� -30��)
    /// </summary>
    [ExecuteAlways]
    public class HexGridAuthoring : MonoBehaviour
    {
        [Header("Layout")]
        public int width = 24;
        public int height = 20;
        public float radius = 0.472f;
        public HexOrientation orientation = HexOrientation.FlatTop;
        public HexOffsetMode offsetMode = HexOffsetMode.OddRow;
        public Transform origin;                // һ������� Plane
        public float tileHeightOffset = 0.01f;

        [Header("Orientation")]
        public bool useOriginYaw = true;        // ���� Plane �� Y ����
        [Range(-180f, 180f)] public float yawOffset = 0f;   // �� Ĭ�ϸ�Ϊ 0��

        [Range(-180f, 180f)] public float yawDegrees = 0f; // ������ Plane���ֶ���

        [Header("Runtime")]
        public bool liveRebuildInPlayMode = false;          // �� ����ʱĬ�ϲ��Զ��ؽ�

        public HexGridLayout Layout { get; private set; }

        // �� �����ֱ��滻ʱ���¼����ⲿ����λê�㣩�ɾݴ���ͶӰ
        public static event Action<HexGridLayout, HexGridLayout> OnLayoutRebuilt;

        void OnEnable() => Rebuild();
        void OnValidate() => Rebuild();

        public void Rebuild()
        {
            var originPos = origin ? origin.position : Vector3.zero;

            float yaw = useOriginYaw && origin
                ? origin.eulerAngles.y + yawOffset
                : yawDegrees;                    // �ֶ�ģʽ

            Layout = new HexGridLayout(width, height, radius,
                                       orientation, offsetMode,
                                       originPos, yaw);
        }
    }
}
