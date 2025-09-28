// File: TGD.CombatV2/AttackCostServiceV2Adapter.cs
using UnityEngine;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    /// �� AttackAction �� StatsV2 / CooldownHubV2 �Խӣ���¼�����غϹ���������
    [DisallowMultipleComponent]
    public sealed class AttackCostServiceV2Adapter : MonoBehaviour, IAttackCostService
    {
        [Header("Refs")]
        public StatsV2 stats;
        public CooldownHubV2 cooldownHub;   // Ŀǰ������ȴ=0��������

        [Header("Overrides (optional)")]
        [Tooltip("�Ƿ���ʱ����ͬ�غϵ��ӣ����츳/�����Ƴ���")]
        public bool ignoreSameTurnPenalty = false;

        int _attacksThisTurn = 0;
        CooldownStoreV2 Store => cooldownHub ? cooldownHub.store : null;

        public bool IsOnCooldown(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg)
        {
            // Ĭ�Ϲ�����ȴ=0
            return false;
        }

        int CalcCost(AttackActionConfigV2 cfg)
        {
            if (cfg == null) return 0;
            float cost = cfg.baseEnergyCost;

            if (cfg.applySameTurnPenalty && !ignoreSameTurnPenalty)
            {
                // ��һ�Σ�+0���ڶ��Σ�+50%�������Σ�+100%...
                // cost = base * (1 + rate * (_attacksThisTurn))
                cost = cfg.baseEnergyCost * (1f + cfg.sameTurnPenaltyRate * Mathf.Max(0, _attacksThisTurn));
            }

            // ��������� int������ȡ��������
            return Mathf.CeilToInt(cost);
        }

        public bool HasEnough(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg)
        {
            if (stats == null || cfg == null) return true; // δ���������в���
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
            // ������ȴĿǰ=0������
            _attacksThisTurn++; // �ۼ�ͬ�غϴ���
        }

        public void ResetForNewTurn() => _attacksThisTurn = 0;

        // ���Ա��
        [ContextMenu("Debug/Reset Attack Count This Turn")]
        void DebugReset() => _attacksThisTurn = 0;
    }
}
