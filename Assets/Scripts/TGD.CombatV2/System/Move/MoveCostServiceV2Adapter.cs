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

        [Header("Options")]
        public string actionIdOverride = ""; // 留空则用 MoveActionConfig.actionId

        CooldownStoreV2 Store => cooldownHub != null ? cooldownHub.store : null;
        string Key(MoveActionConfig cfg)
            => string.IsNullOrEmpty(actionIdOverride) ? (cfg != null ? cfg.actionId : "Move") : actionIdOverride;

        public bool IsOnCooldown(Unit unit, MoveActionConfig cfg)
            => Store != null && Store.RoundsLeft(Key(cfg)) > 0;

        public bool HasEnough(Unit unit, MoveActionConfig cfg)
        {
            if (stats == null || cfg == null) return true; // 未配置则放行，便于测试
            return stats.Energy >= cfg.energyCost;
        }

        public void Pay(Unit unit, MoveActionConfig cfg)
        {
            if (stats == null || cfg == null) return;

            // 扣能量
            stats.Energy = Mathf.Clamp(stats.Energy - cfg.energyCost, 0, stats.MaxEnergy);

            // 开冷却（秒→回合；Move 通常 0）
            if (Store != null && cfg.cooldownSeconds > 0f)
            {
                int rounds = StatsMathV2.CooldownToTurns(Mathf.CeilToInt(cfg.cooldownSeconds));
                Store.Start(Key(cfg), rounds);
            }
        }
        public void RefundSeconds(Unit unit, MoveActionConfig cfg, int seconds)
        {
            if (stats == null || cfg == null || seconds <= 0) return;

            // 约定：MoveActionConfig.energyCost 表示“每秒移动消耗”的能量
            int refund = Mathf.Max(0, cfg.energyCost) * seconds;
            int before = stats.Energy;
            stats.Energy = Mathf.Clamp(stats.Energy + refund, 0, stats.MaxEnergy);

            Debug.Log($"[MoveCost] Refund {seconds}s => +{refund} Energy ({before}->{stats.Energy})",
                      this);
        }
    }
}

