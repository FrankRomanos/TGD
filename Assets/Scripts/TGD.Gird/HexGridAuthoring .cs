using UnityEngine;


namespace TGD.Grid
{
    /// <summary>
    /// Authoring for HexGridLayout. Supports global yaw:
    /// - useOriginYaw: follow the Plane's Y rotation
    /// - yawOffset: extra tweak (Flat-Top 模型常见需要 +30° 或 -30°)
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
        public Transform origin;                // 一般拖你的 Plane
        public float tileHeightOffset = 0.01f;

        [Header("Orientation")]
        public bool useOriginYaw = true;        // 跟随 Plane 的 Y 朝向
        [Range(-180f, 180f)] public float yawOffset = 30f; // 统一偏移（Flat-Top 常用 ±30）
        [Range(-180f, 180f)] public float yawDegrees = 0f; // 若不用 Plane，手动填

        public HexGridLayout Layout { get; private set; }

        void OnEnable() => Rebuild();
        void OnValidate() => Rebuild();

        public void Rebuild()
        {
            var originPos = origin ? origin.position : Vector3.zero;

            float yaw = useOriginYaw && origin
                ? origin.eulerAngles.y + yawOffset
                : yawDegrees;                    // 手动模式

            Layout = new HexGridLayout(width, height, radius,
                                       orientation, offsetMode,
                                       originPos, yaw);
        }
    }
}
