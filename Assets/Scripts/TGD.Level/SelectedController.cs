using UnityEngine;
using UnityEngine.EventSystems;

public class SelectionController : MonoBehaviour
{
    public Camera cam;                 // Ϊ���� Camera.main
    public LayerMask unitMask;         // ֻ���� Units ��
    UnitSelectable current;

    void Awake() { if (!cam) cam = Camera.main; }

    void Update()
    {
        if (EventSystem.current && EventSystem.current.IsPointerOverGameObject()) return;

        if (Input.GetMouseButtonDown(0))
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 1000f, unitMask))
            {
                var u = hit.collider.GetComponentInParent<UnitSelectable>();
                Select(u);
            }
            else
            {
                // �㵽�յأ�ȡ��ѡ��
                Select(null);
            }
        }
    }

    void Select(UnitSelectable u)
    {
        if (current == u) return;
        if (current) current.SetSelected(false);
        current = u;
        if (current) current.SetSelected(true);
    }
}
