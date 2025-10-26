using TGD.Level;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class UnitSelectable : MonoBehaviour
{
    public HexSelectVisual visual; // ���ϣ��������Զ���
    void Awake() { if (!visual) visual = GetComponentInChildren<HexSelectVisual>(); }
    public void SetSelected(bool on) { if (visual) visual.SetVisible(on); }
}