// File: TGD.CombatV2/AttackCostServiceV2Adapter.cs
using UnityEngine;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    /// <summary>
    ///  AttackAction  StatsV2 / CooldownHubV2 Խӣ¼غϹ
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AttackCostServiceV2Adapter : MonoBehaviour, IAttackCostService
    {
        [Header("Refs")]
        public StatsV2 stats;
        public CooldownHubV2 cooldownHub;   // Ŀǰȴ=0

        [Header("Overrides (optional)")]
        [Tooltip("Ƿʱͬغϵӣ츳/Ƴ")]
        public bool ignoreSameTurnPenalty = false;

        [Header("Debug")] //  ̨ӡ
        public bool debugLogCosts = true;   //

        int _attacksThisTurn = 0;
        CooldownStoreV2 Store => cooldownHub ? cooldownHub.store : null;

        public bool IsOnCooldown(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg)
        {
            // ĬϹȴ=0
            return false;
        }

        int CalcCost(AttackActionConfigV2 cfg)
        {
            if (cfg == null) return 0;
            float cost = cfg.baseEnergyCost;

            if (cfg.applySameTurnPenalty && !ignoreSameTurnPenalty)
            {
                // һΣ+0ڶΣ+50%Σ+100%...
                // cost = base * (1 + rate * (_attacksThisTurn))
                cost = cfg.baseEnergyCost * (1f + cfg.sameTurnPenaltyRate * Mathf.Max(0, _attacksThisTurn));
            }

            //  intȡ
            return Mathf.CeilToInt(cost);
        }

        public int PreviewCost(AttackActionConfigV2 cfg) => CalcCost(cfg);

        public bool HasEnough(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg)
        {
            if (stats == null || cfg == null) return true; // δв
            int need = CalcCost(cfg);
            bool ok = stats.Energy >= need;

            if (debugLogCosts && !ok)
                Debug.Log($"[AttackCost] NOT ENOUGH energy (need={need}, have={stats.Energy})", this);

            return ok;
        }

        public void Pay(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg)
        {
            if (stats != null && cfg != null)
            {
                int need = CalcCost(cfg);
                int before = stats.Energy;
                stats.Energy = Mathf.Clamp(stats.Energy - need, 0, stats.MaxEnergy);
                if (debugLogCosts)
                    Debug.Log($"[AttackCost] cost={need}  energy {before}->{stats.Energy}  (attacksThisTurn={_attacksThisTurn})", this);
            }

            _attacksThisTurn++;
        }

        public void Refund(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg)
        {
            if (_attacksThisTurn > 0) _attacksThisTurn--;

            if (stats != null && cfg != null)
            {
                int refund = CalcCost(cfg);
                int before = stats.Energy;
                stats.Energy = Mathf.Clamp(stats.Energy + refund, 0, stats.MaxEnergy);
                if (debugLogCosts)
                    Debug.Log($"[AttackCost] refund={refund}  energy {before}->{stats.Energy}  (attacksThisTurn={_attacksThisTurn})", this);
            }
        }

        public void ResetForNewTurn() => _attacksThisTurn = 0;

        // Ա
        [ContextMenu("Debug/Reset Attack Count This Turn")]
        void DebugReset() => _attacksThisTurn = 0;
    }
}
