// File: TGD.CombatV2/AttackCostServiceV2Adapter.cs
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

        [Header("Overrides (optional)")]
        [Tooltip("是否临时忽略同回合叠加（被天赋/技能移除）")]
        public bool ignoreSameTurnPenalty = false;

        int _attacksThisTurn = 0;
        CooldownStoreV2 Store => cooldownHub ? cooldownHub.store : null;

        public bool IsOnCooldown(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg)
        {
            // 默认攻击冷却=0
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
            if (stats == null || cfg == null) return true; // 未配面板则放行测试
            int need = CalcCost(cfg);
            return stats.Energy >= need;
        }

        public void Pay(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg)
        {
            if (stats != null && cfg != null)
            {
                int need = CalcCost(cfg);
                stats.Energy = Mathf.Clamp(stats.Energy - need, 0, stats.MaxEnergy);
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
