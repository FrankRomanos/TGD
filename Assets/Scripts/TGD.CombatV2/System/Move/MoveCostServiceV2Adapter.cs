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

        CooldownStoreSecV2 Store => cooldownHub != null ? cooldownHub.secStore : null;
        string Key(MoveActionConfig cfg)
            => string.IsNullOrEmpty(actionIdOverride) ? (cfg != null ? cfg.actionId : "Move") : actionIdOverride;

        public bool IsOnCooldown(Unit unit, MoveActionConfig cfg)
        {
            if (turnManager != null)
                return false;
            string key = Key(cfg);
            return Store != null && !Store.Ready(key);
        }

        public bool HasEnough(Unit unit, MoveActionConfig cfg)
        {
            if (cfg == null) return true;
            if (turnManager != null)
                return true;

            int need = Mathf.Max(0, cfg.energyCost);
            if (stats == null) return true;
            return stats.Energy >= need;
        }

        public void Pay(Unit unit, MoveActionConfig cfg)
        {
            if (cfg == null) return;
            if (turnManager != null)
                return;
            int need = Mathf.Max(0, cfg.energyCost);
            string key = Key(cfg);

            if (stats != null)
                stats.Energy = Mathf.Clamp(stats.Energy - need, 0, stats.MaxEnergy);

            // 开冷却（秒→回合；Move 通常 0）
            if (Store != null && cfg.cooldownSeconds > 0f)
            {
                int seconds = Mathf.CeilToInt(cfg.cooldownSeconds);
                Store.StartSeconds(key, seconds);
            }
        }
        public void RefundSeconds(Unit unit, MoveActionConfig cfg, int seconds)
        {
            if (cfg == null || seconds <= 0) return;

            if (turnManager != null)
                return;
            
            if (stats == null) return;
            int refund = Mathf.Max(0, cfg.energyCost) * seconds;
            int before = stats.Energy;
            stats.Energy = Mathf.Clamp(stats.Energy + refund, 0, stats.MaxEnergy);

            Debug.Log($"[MoveCost] Refund {seconds}s => +{refund} Energy ({before}->{stats.Energy})",
                      this);
        }
    }
}

