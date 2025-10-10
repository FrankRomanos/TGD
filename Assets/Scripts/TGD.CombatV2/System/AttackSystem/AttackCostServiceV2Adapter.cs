﻿// File: TGD.CombatV2/AttackCostServiceV2Adapter.cs
using UnityEngine;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    /// 将 AttackAction 与 StatsV2 / CooldownHubV2 对接；记录“本回合攻击次数”
    [DisallowMultipleComponent]
    public sealed class AttackCostServiceV2Adapter : MonoBehaviour, IAttackCostService
    {
        [Header("Refs")]
        public StatsV2 stats;
        public CooldownHubV2 cooldownHub;   // 目前攻击冷却=0，可留空
        public TurnManagerV2 turnManager;
        public UnitRuntimeContext ctx;

        [Header("Overrides (optional)")]
        [Tooltip("是否临时忽略同回合叠加（被天赋/技能移除）")]
        public bool ignoreSameTurnPenalty = false;

        [Header("Debug")] // ★ 新增：控制台打印开关
        public bool debugLogCosts = true;   // ★

        int _attacksThisTurn = 0;
        CooldownStoreSecV2 Store => cooldownHub ? cooldownHub.secStore : null;

        public bool IsOnCooldown(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg)
        {
            return false;
        }

        int CalcCost(AttackActionConfigV2 cfg)
        {
            if (cfg == null) return 0;
            float cost = cfg.baseEnergyCost;

            if (cfg.applySameTurnPenalty && !ignoreSameTurnPenalty)
            {
                // 第一次：+0；第二次：+50%；第三次：+100%...
                // cost = base * (1 + rate * (_attacksThisTurn))
                cost = cfg.baseEnergyCost * (1f + cfg.sameTurnPenaltyRate * Mathf.Max(0, _attacksThisTurn));
            }

            // 你家能量是 int，向上取整更保守
            return Mathf.CeilToInt(cost);
        }

        public bool HasEnough(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg)
        {
            if (turnManager != null)
                return true;
            int need = CalcCost(cfg);

            if (stats == null || cfg == null) return true; // δв
            bool has = stats.Energy >= need;
            // ★ 可选：不足时也打一条
            if (debugLogCosts && !has)
                Debug.Log($"[AttackCost] NOT ENOUGH energy (need={need}, have={stats.Energy})", this);

            return has;
        }

        public void Pay(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg)
        {
            int need = CalcCost(cfg);
            if (turnManager != null)
            {
                _attacksThisTurn++;
                if (debugLogCosts)
                    Debug.Log($"[AttackCost] defer cost={need} (turn attacks={_attacksThisTurn})", this);
                return;
            }

            if (stats != null && cfg != null)
            {
                int before = stats.Energy;
                stats.Energy = Mathf.Clamp(stats.Energy - need, 0, stats.MaxEnergy);
                // ★ 打印你要的日志格式
                if (debugLogCosts)
                    Debug.Log($"[AttackCost] cost={need}  energy {before}->{stats.Energy}  (attacksThisTurn={_attacksThisTurn})", this);
            }
           
            // 攻击冷却目前=0，跳过
            _attacksThisTurn++; // 累计同回合次数
        }

        public void ResetForNewTurn() => _attacksThisTurn = 0;

        // 测试便捷
        [ContextMenu("Debug/Reset Attack Count This Turn")]
        void DebugReset() => _attacksThisTurn = 0;
    }
}
