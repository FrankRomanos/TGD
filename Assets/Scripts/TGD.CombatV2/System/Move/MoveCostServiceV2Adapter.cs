using UnityEngine;
using TGD.CoreV2; //  CoreV2HexBoard.asmdef Ӷ CoreV2.asmdef ã
using TGD.HexBoard;

namespace TGD.CombatV2
{
    /// <summary>
    ///  HexClickMover ͨ IMoveCostService  StatsV2 + CooldownStoreV2
    /// </summary>
    public sealed class MoveCostServiceV2Adapter : MonoBehaviour, IMoveCostService
    {
        [Header("Refs")]
        public StatsV2 stats;              // λ CoreV2
        public CooldownHubV2 cooldownHub;  // ͬ/ͬһ
        public TurnManagerV2 turnManager;
        public UnitRuntimeContext ctx;

        [Header("Options")]
        public string actionIdOverride = ""; //  MoveActionConfig.actionId

        CooldownStoreV2 Store => cooldownHub != null ? cooldownHub.store : null;
        string Key(MoveActionConfig cfg)
            => string.IsNullOrEmpty(actionIdOverride) ? (cfg != null ? cfg.actionId : "Move") : actionIdOverride;

        public bool IsOnCooldown(Unit unit, MoveActionConfig cfg)
        {
            string key = Key(cfg);
            if (turnManager != null && unit != null)
            {
                var cds = turnManager.GetCooldowns(unit);
                if (cds != null)
                    return !cds.Ready(key);
            }
            return Store != null && Store.RoundsLeft(key) > 0;
        }

        public bool HasEnough(Unit unit, MoveActionConfig cfg)
        {
            if (cfg == null) return true;
            int need = Mathf.Max(0, cfg.energyCost);

            if (turnManager != null && unit != null)
            {
                var pool = turnManager.GetResources(unit);
                if (pool != null)
                    return pool.Has("Energy", need);
            }

            if (stats == null) return true; // δУڲ
            return stats.Energy >= need;
        }

        public void Pay(Unit unit, MoveActionConfig cfg)
        {
            if (cfg == null) return;

            int need = Mathf.Max(0, cfg.energyCost);
            string key = Key(cfg);

            if (turnManager != null && unit != null)
            {
                var pool = turnManager.GetResources(unit);
                pool?.Spend("Energy", need, "Move");

                if (cfg.cooldownSeconds > 0f)
                {
                    int seconds = Mathf.CeilToInt(cfg.cooldownSeconds);
                    var cds = turnManager.GetCooldowns(unit);
                    cds?.StartSeconds(key, seconds);
                }
                return;
            }

            if (stats != null)
                stats.Energy = Mathf.Clamp(stats.Energy - need, 0, stats.MaxEnergy);

            if (Store != null && cfg.cooldownSeconds > 0f)
            {
                int rounds = StatsMathV2.CooldownToTurns(Mathf.CeilToInt(cfg.cooldownSeconds));
                Store.Start(key, rounds);
            }
        }

        public void RefundSeconds(Unit unit, MoveActionConfig cfg, int seconds)
        {
            if (cfg == null || seconds <= 0) return;

            int refund = Mathf.Max(0, cfg.energyCost) * seconds;

            if (turnManager != null && unit != null)
            {
                var pool = turnManager.GetResources(unit);
                pool?.Refund("Energy", refund, "MoveRefund");
                return;
            }

            if (stats == null) return;

            int before = stats.Energy;
            stats.Energy = Mathf.Clamp(stats.Energy + refund, 0, stats.MaxEnergy);

            Debug.Log($"[MoveCost] Refund {seconds}s => +{refund} Energy ({before}->{stats.Energy})",
                      this);
        }
    }
}
