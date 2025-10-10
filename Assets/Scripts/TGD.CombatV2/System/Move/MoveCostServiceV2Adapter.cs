using UnityEngine;
using TGD.CoreV2; // 引用 CoreV2（HexBoard.asmdef 需添加对 CoreV2.asmdef 的引用）
using TGD.HexBoard;

namespace TGD.CombatV2
{
    /// <summary>
    /// 让 HexClickMover 通过 IMoveCostService 接入 StatsV2 + CooldownStoreV2。
    /// </summary>
    public sealed class MoveCostServiceV2Adapter : MonoBehaviour, IMoveCostService
    {
        [Header("Refs")]
        public StatsV2 stats;              // 本单位的 CoreV2 面板
        public CooldownHubV2 cooldownHub;  // 场景里同物体/同父物体挂一份
        public TurnManagerV2 turnManager;
        public UnitRuntimeContext ctx;

        [Header("Options")]
        public string actionIdOverride = ""; // 留空则用 MoveActionConfig.actionId

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

            if (stats == null) return true; 
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

            // 开冷却（秒→回合；Move 通常 0）
            if (Store != null && cfg.cooldownSeconds > 0f)
            {
                int turns = StatsMathV2.CooldownToTurns(Mathf.CeilToInt(cfg.cooldownSeconds));
                Store.Start(key, turns);
            }
        }
        public void RefundSeconds(Unit unit, MoveActionConfig cfg, int seconds)
        {
            if (stats == null || cfg == null || seconds <= 0) return;

            // 约定：MoveActionConfig.energyCost 表示“每秒移动消耗”的能量
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

