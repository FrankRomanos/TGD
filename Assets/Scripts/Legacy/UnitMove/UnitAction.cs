using System;
using UnityEngine.EventSystems;
using UnityEngine;

public class UnitAction : MonoBehaviour
{
    public static UnitAction Instance { get; private set; }
    public event EventHandler OnSelectedUnitChanged;
    public event EventHandler OnSelectedActionChanged;
    public event EventHandler<bool> OnBusyChanged;
    public event EventHandler OnActionStarted;

    [SerializeField] private LayerMask UnitLayerMask;

    private Unit selectedUnit;
    private BaseAction selectedAction;
    private bool isBusy;

    private void Start()
    {
        // ��Ϸ��ʼʱ��ѡ���κε�λ
        selectedUnit = null;
        selectedAction = null;
        // �����¼�����UI��
        OnSelectedUnitChanged?.Invoke(this, EventArgs.Empty);
        OnSelectedActionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Awake()
    {
        if (Instance != null)
        {
            Debug.LogError("There's more than one UnitAction!" + transform + "-" + Instance);
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        if (isBusy)
        {
            return;
        }

        if (!TurnSystem.Instance.IsPlayerTurn())
        {
            return;
        }
        if (EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        if (TryHandleUnitSelection())
        {
            return;
        }

        HandleSelectedAction();
    }

    private void HandleSelectedAction()
    {
        if (Input.GetMouseButtonUp(0))
        {
            // ���û��ѡ�е�λ��ֱ�ӷ���
            if (selectedUnit == null)
            {
                return;
            }

            GridPosition mouseGridPosition = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());
            if (selectedAction != null && selectedAction.IsValidActionGridPosition(mouseGridPosition))
            {
                if (selectedUnit.TrySpendActionPointsToTakeAction(selectedAction))
                {
                    SetBusy();
                    selectedAction.TakeAction(mouseGridPosition, ClearBusy);
                    OnActionStarted?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    private void SetBusy()
    {
        isBusy = true;
        OnBusyChanged?.Invoke(this,isBusy);
    }

    private void ClearBusy()
    {
        isBusy = false;
        OnBusyChanged?.Invoke(this,isBusy);
    }

    private bool TryHandleUnitSelection()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit raycastHit, float.MaxValue, UnitLayerMask))
            {
                if (raycastHit.transform.TryGetComponent<Unit>(out Unit unit))
                {
                    if (unit == selectedUnit)
                    {
                        // Unit is already selected
                        return false;
                    }

                    if (unit.IsEnemy())
                    {
                        return false ;
                    }
                    SetSelectedUnit(unit);
                    return true;
                }
            }
        }
        return false;
    }

    private void SetSelectedUnit(Unit unit)
    {
        selectedUnit = unit;
        // ѡ�е�λʱĬ�ϲ�ִ���κζ���
        SetSelectedAction(null);

        OnSelectedUnitChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelectedAction(BaseAction baseAction)
    {
        selectedAction = baseAction;
        OnSelectedActionChanged?.Invoke(this, EventArgs.Empty);
    }

    public Unit GetSelectedUnit()
    {
        return selectedUnit;
    }

    public BaseAction GetSelectedAction()
    {
        return selectedAction;
    }
}
