using TGD.Grid;
using TGD.Level;
using UnityEngine;

public class HexSelectVisual : MonoBehaviour
{
    [Header("Refs")]
    public HexGridAuthoring grid;           // �� HexGridRoot
    public GameObject ringPrefab;           // �� FX_HexSelectRing ����
    public Transform fxParent;              // ��ѡ��FX ������������ڵ�λ�ϣ�

    [Header("Height / Placement")]
    public bool placeByRaycast = true;      // �����ȡ��ߵ㣨��׼��
    public LayerMask groundMask;            // ֻ�� Decor����ש/���أ�
    public float probeHeight = 2f;          // ��������̽��߶�
    public float hoverOffset = 0.03f;       // ���̧�𣬱�����˸

    [Header("Rotation / Size")]
    [Range(-180, 180)] public float ringYawOffset = 0f; // ģ�Ͳ��򣬳��� ��30
    public bool fitToGridRadius = true;     // �� grid.radius �����С
    public float ringScaleMul = 1.0f;       // ������Ļ������ٳˣ�>1 �Ŵ�

    [Header("Layer (safe)")]
    public bool setRingLayer = true;        // �Ƿ�������õ�ĳ��
    public string ringLayerName = "Decor";  // ����Ŀ���޸ò㣬���Զ����������ٱ���

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

        // ���� ��ȫ���ò㣨��ֹ NameToLayer ���� -1 ��������
        if (setRingLayer && !string.IsNullOrEmpty(ringLayerName))
        {
            int l = LayerMask.NameToLayer(ringLayerName);
            if (l >= 0 && l <= 31) SetLayerRecursively(go, l);
            // ��δ���øò������򱣳�Ԥ���Դ��㣬��ǿ��
        }

        // �������� Collider�����⵲���/������
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

        // ��ʱ����ȷ����ȡ�� bounds
        bool on = ring.gameObject.activeSelf;
        if (!on) ring.gameObject.SetActive(true);

        var w = ringRenderer.bounds.size.x; // ������
        if (w > 1e-5f)
        {
            var target = 2f * grid.radius;  // Flat-Top������ = 2r
            float s = (target / w) * Mathf.Max(0.0001f, ringScaleMul);
            ring.localScale *= s;
            cachedRadius = grid.radius;
        }

        if (!on) ring.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (!visible || grid == null || grid.Layout == null) return;

        // 1) �ҵ�λ���ڸ����ģ����Ӹ߶ȣ�
        var c = grid.Layout.GetCoordinate(transform.position);
        var p = grid.Layout.GetWorldPosition(c, 0f);

        // 2) �߶ȣ��������ߣ�ʧ���߶��ף�ʼ�հѻ�̧�����棩
        float baseY = grid.origin ? grid.origin.position.y : 0f;
        float finalY = baseY + grid.tileHeightOffset + hoverOffset;

        if (placeByRaycast)
        {
            Vector3 from = new Vector3(p.x, p.y + probeHeight, p.z);
            if (Physics.Raycast(from, Vector3.down, out var hit,
                                probeHeight * 2f, groundMask,
                                QueryTriggerInteraction.Collide)) // ���� Trigger Ҳ��
            {
                finalY = hit.point.y + hoverOffset;
            }
        }

        p.y = finalY;

        // 3) ��ת������
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