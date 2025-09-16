using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class Unit : MonoBehaviour
{
    [Header("===== ������λ��Ϣ��ԭʼ�� =====")]
    public string UnitName;
    private const int ACTION_POINTS_MAX = 6;

    [SerializeField] private bool isEnemy;

    public static event EventHandler OnAnyActionPointsChanged;
    private HashSet<string> _usedPreSkillIDs = new();

    private int _remainingTurnTime;
    private GridPosition gridPosition;
    private HealthSystem healthSystem;
    private MoveAction moveAction;

    private BaseAction[] baseActionArray;
    private int actionPoints = ACTION_POINTS_MAX;


    private void Awake()
    {
        healthSystem = GetComponent<HealthSystem>();
        moveAction = GetComponent<MoveAction>();

        baseActionArray = GetComponents<BaseAction>();
    }

    private void Start()
    {
        this.gridPosition = LevelGrid.Instance.GetGridPosition(transform.position);
        LevelGrid.Instance.AddUnitAtGridPosition(gridPosition, this);
        TurnSystem.Instance.OnTurnChanged += TurnSystem_OnTurnChanged;

        healthSystem.OnDead += HealthSystem_OnDead;
    }

    private void Update()
    {
        GridPosition newGridPosition = LevelGrid.Instance.GetGridPosition(transform.position);
        if (newGridPosition != gridPosition)
        {
            //Unit change Grid Position!
            LevelGrid.Instance.UnitMoveGridPosition(this, gridPosition, newGridPosition);
            gridPosition = newGridPosition;
        }
    }

    // ��顰ǰ�ü����Ƿ���ʹ�á���ͨ�� SkillID �жϣ�


    // ������ڵ�ǰ�ü���ID������������Ч��
    private void ClearExpiredPreSkillIDs()
    {
        // ����Ҫ��ȷ���Ƶ���ID�Ĺ���ʱ�䣬�ɸ��� Dictionary<string, float> �洢ʱ���
        _usedPreSkillIDs.Clear();
    }

    // �غϽ���ʱ��������ǰ�ü���ID
    public void ClearUsedPreSkill()
    {
        _usedPreSkillIDs.Clear();
    }

    public MoveAction GetMoveAction()
    {
        return moveAction;
    }


    public GridPosition GetGridPosition()
    {
        return gridPosition;
    }

    public Vector3 GetWorldPosition()
    {
        return transform.position;
    }

    public BaseAction[] GetBaseActionArray()
    {
        return baseActionArray;
    }

    public bool TrySpendActionPointsToTakeAction(BaseAction baseAction)
    {
        if (CanSpendActionPointsToTakeAction(baseAction))
        {
            SpendActionPoints(baseAction.GetActionPointsCost());
            return true;
        }
        else
        {
            return false;
        }
    }
    public bool CanSpendActionPointsToTakeAction(BaseAction baseAction)
    {
        if (actionPoints >= baseAction.GetActionPointsCost())
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    private void SpendActionPoints(int amount)
    {
        actionPoints -= amount;
        OnAnyActionPointsChanged?.Invoke(this, EventArgs.Empty);
    }

    public int GetActionPoints()
    {
        return actionPoints;
    }

    private void TurnSystem_OnTurnChanged(object sender, EventArgs e)
    {
        if ((IsEnemy() && !TurnSystem.Instance.IsPlayerTurn()) ||
            (!IsEnemy() && TurnSystem.Instance.IsPlayerTurn()))
        {
            actionPoints = ACTION_POINTS_MAX;
            OnAnyActionPointsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsEnemy()
    {
        return isEnemy;
    }

    public void Damage(int damageAmount)
    {
        healthSystem.Damage(damageAmount);
    }

    private void HealthSystem_OnDead(object sender, EventArgs e)
    {
        LevelGrid.Instance.RemoveUnitAtGridPosition(gridPosition, this);

        Destroy(gameObject);
    }
    public void RefundActionPoints(int amount)
    {
        // �����ж��㳬�����ֵ����ѡ�߼���������Ϸ���������
        actionPoints = Mathf.Min(actionPoints + amount, ACTION_POINTS_MAX);
        // �����¼���֪ͨUI�����ж�����ʾ����SpendActionPoints����һ�£�
        OnAnyActionPointsChanged?.Invoke(this, EventArgs.Empty);
    }
    public int GetRemainingTurnTime() => _remainingTurnTime;

    // ����ʣ��ʱ�䣨int��
    public void SetRemainingTurnTime(int time)
    {
        _remainingTurnTime = Mathf.Max(0, time);
    }


}