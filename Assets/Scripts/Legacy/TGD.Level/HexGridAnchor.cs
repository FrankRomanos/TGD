using UnityEngine;
using TGD.Grid;

public class HexGridAnchor : MonoBehaviour
{
    public HexGridAuthoring authoring;       // 指向场景里的 Authoring
    public HexCoord coordinate = default;    // 当前逻辑坐标（可在生成时写入）
    public bool snapOnStart = true;

    void OnEnable()
    {
        HexGridAuthoring.OnLayoutRebuilt += HandleLayoutRebuilt;
    }

    void OnDisable()
    {
        HexGridAuthoring.OnLayoutRebuilt -= HandleLayoutRebuilt;
    }

    void Start()
    {
        if (snapOnStart && authoring && authoring.Layout != null)
        {
            // 如果 coordinate 还是 (0,0) 且物体不是放在原点，则先由世界坐标求一次格点
            if (coordinate.Equals(HexCoord.Zero) && transform.position != Vector3.zero)
                coordinate = authoring.Layout.GetCoordinate(transform.position); // 先解算
            SnapToGrid();
        }
    }

    public void SnapToGrid()
    {
        if (!authoring || authoring.Layout == null) return;
        var world = authoring.Layout.GetWorldPosition(coordinate, authoring.tileHeightOffset);
        transform.position = world;
    }

    void HandleLayoutRebuilt(HexGridLayout oldLayout, HexGridLayout newLayout)
    {
        if (!authoring || newLayout == null) return;

        // 如果有旧布局，先用“旧布局”解算坐标，避免因为 yaw 变化而错位
        if (oldLayout != null)
            coordinate = oldLayout.GetCoordinate(transform.position);  // 世界 -> 旧格
        // 再用新布局把坐标放回去
        var world = newLayout.GetWorldPosition(coordinate, authoring.tileHeightOffset);
        transform.position = world;
    }
}
