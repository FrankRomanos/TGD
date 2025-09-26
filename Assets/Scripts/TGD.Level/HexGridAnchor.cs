using UnityEngine;
using TGD.Grid;

public class HexGridAnchor : MonoBehaviour
{
    public HexGridAuthoring authoring;       // ָ�򳡾���� Authoring
    public HexCoord coordinate = default;    // ��ǰ�߼����꣨��������ʱд�룩
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
            // ��� coordinate ���� (0,0) �����岻�Ƿ���ԭ�㣬����������������һ�θ��
            if (coordinate.Equals(HexCoord.Zero) && transform.position != Vector3.zero)
                coordinate = authoring.Layout.GetCoordinate(transform.position); // �Ƚ���
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

        // ����оɲ��֣����á��ɲ��֡��������꣬������Ϊ yaw �仯����λ
        if (oldLayout != null)
            coordinate = oldLayout.GetCoordinate(transform.position);  // ���� -> �ɸ�
        // �����²��ְ�����Ż�ȥ
        var world = newLayout.GetWorldPosition(coordinate, authoring.tileHeightOffset);
        transform.position = world;
    }
}
