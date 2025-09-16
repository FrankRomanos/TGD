using System;
using Unity.VisualScripting;
using UnityEngine;

public class UnitSelectedVisual : MonoBehaviour
{
    [SerializeField] private Unit unit;
    private MeshRenderer MeshRenderer;

    private void Awake()
    {
        MeshRenderer = GetComponent<MeshRenderer>();
    }

    private void Start()
    {
        UnitAction.Instance.OnSelectedUnitChanged += UnitAction_OnSelectedUnitChanged;
        UpdateVisual();
    }

    private void UnitAction_OnSelectedUnitChanged(object sender, EventArgs empty)
    {
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (UnitAction.Instance.GetSelectedUnit() == unit)
        {
            MeshRenderer.enabled = true;
        }
        else
        {
            MeshRenderer.enabled = false;
        }
    }

    private void OnDestroy()
    {
        UnitAction.Instance.OnSelectedUnitChanged -= UnitAction_OnSelectedUnitChanged;
    }

}
