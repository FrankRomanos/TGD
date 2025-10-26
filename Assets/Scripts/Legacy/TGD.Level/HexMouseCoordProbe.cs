using TGD.Grid;
using TGD.Level;
using UnityEngine;
// 如果要在 UI 显示，把下面这行解开并在 Inspector 里拖一个 TMP_Text
// using TMPro;

public class HexMouseCoordProbe : MonoBehaviour
{
    [Header("Refs")]
    public HexGridAuthoring grid;     // 拖 HexGridRoot（里有 Layout/Origin/Yaw）
    public Camera cam;                // 不填就用 Camera.main
    public Transform highlight;       // 可选：拖一个环（如 MetalBorder Glow）或小圆片

    [Header("Ray Mode")]
    public bool useMathPlane = true;  // ✅ 推荐：用数学平面，不受地形影响
    public string groundLayer = "MousePlane"; // 物理射线时命中的层

    [Header("Misc")]
    public bool clampToGrid = true;   // 鼠标出了边界时，是否夹回网格范围
    // public TMP_Text coordText;     // 可选：在 UI 上显示

    int layerMask;
    HexCoord lastCoord;
    Vector3 lastPos;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        layerMask = LayerMask.GetMask(groundLayer);
        lastCoord = new HexCoord(0, 0);
    }

    void Update()
    {
        if (!grid || grid.Layout == null || cam == null) return;

        var ray = cam.ScreenPointToRay(Input.mousePosition);
        Vector3 hitPoint;
        bool hit;

        if (useMathPlane)
        {
            // 用“数学平面”：水平，过 origin 的 Y。地形再高也不影响。
            float y0 = grid.origin ? grid.origin.position.y : 0f;
            var plane = new Plane(Vector3.up, new Vector3(0f, y0, 0f));
            hit = plane.Raycast(ray, out float t);
            hitPoint = ray.origin + ray.direction * t;
        }
        else
        {
            // 用物理射线：只打 MousePlane 层
            hit = Physics.Raycast(ray, out var hitInfo, 5000f, layerMask);
            hitPoint = hitInfo.point;
        }

        if (!hit) return;

        // 世界点 → 六边格坐标（内部已处理全局 Yaw）
        var coord = grid.Layout.GetCoordinate(hitPoint);

        if (clampToGrid && !grid.Layout.Contains(coord))
        {
            // 从网格中心向鼠标方向夹回边界
            var center = new HexCoord(grid.Layout.Width / 2, grid.Layout.Height / 2);
            coord = grid.Layout.ClampToBounds(center, coord);
        }

        var pos = grid.Layout.GetWorldPosition(coord, grid.tileHeightOffset);

        // 高亮跟随
        if (highlight) highlight.position = pos;

        lastCoord = coord;
        lastPos = pos;

        // 如果需要 UI 显示：
        // if (coordText) coordText.text = $"Hex: ({coord.Q},{coord.R})  World: {pos:F2}";
        // 也可临时看日志：
        // Debug.Log($"Hover: {coord}  world:{pos}");
    }

    void OnGUI()
    {
        // 没上 TMP 的简易显示
        GUI.Label(new Rect(10, 10, 360, 22),
            $"Hex ({lastCoord.Q},{lastCoord.R})   World {lastPos.ToString("F2")}");
    }
}

