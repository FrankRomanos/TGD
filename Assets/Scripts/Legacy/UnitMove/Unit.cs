using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class Unit : MonoBehaviour
{
    [Header("===== 基础单位信息（原始） =====")]
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

    // 检查“前置技能是否已使用”（通过 SkillID 判断）


    // 清理过期的前置技能ID（避免永久有效）
    private void ClearExpiredPreSkillIDs()
    {
        // 若需要精确控制单个ID的过期时间，可改用 Dictionary<string, float> 存储时间戳
        _usedPreSkillIDs.Clear();
    }

    // 回合结束时清理所有前置技能ID
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
        // 避免行动点超过最大值（可选逻辑，根据游戏规则决定）
        actionPoints = Mathf.Min(actionPoints + amount, ACTION_POINTS_MAX);
        // 触发事件，通知UI更新行动点显示（与SpendActionPoints保持一致）
        OnAnyActionPointsChanged?.Invoke(this, EventArgs.Empty);
    }
    public int GetRemainingTurnTime() => _remainingTurnTime;

    // 设置剩余时间（int）
    public void SetRemainingTurnTime(int time)
    {
        _remainingTurnTime = Mathf.Max(0, time);
    }


}