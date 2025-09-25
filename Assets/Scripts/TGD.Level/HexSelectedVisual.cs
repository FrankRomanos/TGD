using TGD.Grid;
using TGD.Level;
using UnityEngine;

public class HexSelectVisual : MonoBehaviour
{
    [Header("Refs")]
    public HexGridAuthoring grid;           // 拖 HexGridRoot
    public GameObject ringPrefab;           // 拖 FX_HexSelectRing 变体
    public Transform fxParent;              // 可选：FX 根（不填则挂在单位上）

    [Header("Height / Placement")]
    public bool placeByRaycast = true;      // 打地面取最高点（精准）
    public LayerMask groundMask;            // 只勾 Decor（地砖/草沿）
    public float probeHeight = 2f;          // 从上往下探测高度
    public float hoverOffset = 0.03f;       // 离地抬起，避免闪烁

    [Header("Rotation / Size")]
    [Range(-180, 180)] public float ringYawOffset = 0f; // 模型差向，常用 ±30
    public bool fitToGridRadius = true;     // 按 grid.radius 适配大小
    public float ringScaleMul = 1.0f;       // 在适配的基础上再乘（>1 放大）

    [Header("Layer (safe)")]
    public bool setRingLayer = true;        // 是否给环设置到某层
    public string ringLayerName = "Decor";  // 若项目里无该层，会自动跳过，不再报错

    Transform ring;
    Renderer ringRenderer;
    float cachedRadius = -1f;
    bool visible;
    float _appliedScaleMul = -1f;

    void Ensure()
    {
        if (ring || !ringPrefab) return;

        var parent = fxParent ? fxParent : transform;
        var go = Instantiate(ringPrefab, parent);
        ring = go.transform;

        // —— 安全设置层（防止 NameToLayer 返回 -1 报错）——
        if (setRingLayer && !string.IsNullOrEmpty(ringLayerName))
        {
            int l = LayerMask.NameToLayer(ringLayerName);
            if (l >= 0 && l <= 31) SetLayerRecursively(go, l);
            // 若未配置该层名，则保持预制自带层，不强设
        }

        // 禁用所有 Collider，避免挡鼠标/被命中
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;

        ringRenderer = go.GetComponentInChildren<Renderer>(true);
        go.SetActive(false);
    }

    void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
    }

    float GetGridYaw()
    {
        if (!grid) return 0f;
        if (grid.useOriginYaw && grid.origin)
            return grid.origin.eulerAngles.y + grid.yawOffset;
        return grid.yawDegrees;
    }

    void FitToRadiusNow()
    {
        if (!fitToGridRadius || ringRenderer == null || grid == null) return;
        if (Mathf.Approximately(cachedRadius, grid.radius)) return;

        // 临时激活确保能取到 bounds
        bool on = ring.gameObject.activeSelf;
        if (!on) ring.gameObject.SetActive(true);

        var w = ringRenderer.bounds.size.x; // 世界宽度
        if (w > 1e-5f)
        {
            var target = 2f * grid.radius;  // Flat-Top：横向 = 2r
            float s = (target / w) * Mathf.Max(0.0001f, ringScaleMul);
            ring.localScale *= s;
            cachedRadius = grid.radius;
        }

        if (!on) ring.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (!visible || grid == null || grid.Layout == null) return;

        // 1) 找单位所在格中心（不加高度）
        var c = grid.Layout.GetCoordinate(transform.position);
        var p = grid.Layout.GetWorldPosition(c, 0f);

        // 2) 高度：优先射线，失败走兜底（始终把环抬出地面）
        float baseY = grid.origin ? grid.origin.position.y : 0f;
        float finalY = baseY + grid.tileHeightOffset + hoverOffset;

        if (placeByRaycast)
        {
            Vector3 from = new Vector3(p.x, p.y + probeHeight, p.z);
            if (Physics.Raycast(from, Vector3.down, out var hit,
                                probeHeight * 2f, groundMask,
                                QueryTriggerInteraction.Collide)) // 命中 Trigger 也算
            {
                finalY = hit.point.y + hoverOffset;
            }
        }

        p.y = finalY;

        // 3) 旋转与缩放
        var yaw = GetGridYaw() + ringYawOffset;
        ring.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
        FitToRadiusNow();
    }

    public void SetVisible(bool on)
    {
        Ensure();
        visible = on;
        if (ring) ring.gameObject.SetActive(on);
    }
}