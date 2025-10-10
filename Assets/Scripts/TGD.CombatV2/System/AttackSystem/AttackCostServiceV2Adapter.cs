// File: TGD.CombatV2/AttackCostServiceV2Adapter.cs
using UnityEngine;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    ///  AttackAction  StatsV2 / CooldownHubV2 Խӣ¼غϹ
    [DisallowMultipleComponent]
    public sealed class AttackCostServiceV2Adapter : MonoBehaviour, IAttackCostService
    {
        [Header("Refs")]
        public StatsV2 stats;
        public CooldownHubV2 cooldownHub;   // Ŀǰȴ=0
        public TurnManagerV2 turnManager;
        public UnitRuntimeContext ctx;

        [Header("Overrides (optional)")]
        [Tooltip("Ƿʱͬغϵӣ츳/Ƴ")]
        public bool ignoreSameTurnPenalty = false;

        [Header("Debug")] //  ̨ӡ
        public bool debugLogCosts = true;   //

        int _attacksThisTurn = 0;
        CooldownStoreV2 Store => cooldownHub ? cooldownHub.store : null;

        public bool IsOnCooldown(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg)
        {
            if (turnManager != null && unit != null)
            {
                var cds = turnManager.GetCooldowns(unit);
                if (cds != null)
                    return !cds.Ready(cfg != null ? cfg.name : "Attack");
            }
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

        public bool HasEnough(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg)
        {
            int need = CalcCost(cfg);
            if (turnManager != null && unit != null)
            {
                var pool = turnManager.GetResources(unit);
                if (pool != null)
                {
                    bool ok = pool.Has("Energy", need);
                    if (debugLogCosts && !ok)
                        Debug.Log($"[AttackCost] NOT ENOUGH energy (need={need})", this);
                    return ok;
                }
            }

            if (stats == null || cfg == null) return true; // δв
            bool has = stats.Energy >= need;

            if (debugLogCosts && !has)
                Debug.Log($"[AttackCost] NOT ENOUGH energy (need={need}, have={stats.Energy})", this);

            return has;
        }

        public void Pay(TGD.HexBoard.Unit unit, AttackActionConfigV2 cfg)
        {
            int need = CalcCost(cfg);
            if (turnManager != null && unit != null)
            {
                var pool = turnManager.GetResources(unit);
                pool?.Spend("Energy", need, "Attack");
                // Ĭûcooldown seconds, ȴʱԤ留
                _attacksThisTurn++;
                if (debugLogCosts)
                    Debug.Log($"[AttackCost] cost={need} (turn attacks={_attacksThisTurn})", this);
                return;
            }

            if (stats != null && cfg != null)
            {
                int before = stats.Energy;
                stats.Energy = Mathf.Clamp(stats.Energy - need, 0, stats.MaxEnergy);
                //  ӡҪ־ʽ
                if (debugLogCosts)
                    Debug.Log($"[AttackCost] cost={need}  energy {before}->{stats.Energy}  (attacksThisTurn={_attacksThisTurn})", this);
            }

            // ȴĿǰ=0
            _attacksThisTurn++; // ۼͬغϴ
        }

        public void ResetForNewTurn() => _attacksThisTurn = 0;

        // Ա
        [ContextMenu("Debug/Reset Attack Count This Turn")]
        void DebugReset() => _attacksThisTurn = 0;
    }
}
