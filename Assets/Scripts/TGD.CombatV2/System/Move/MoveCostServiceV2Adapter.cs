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
        public string skillIdOverride = ""; // 留空则用单位配置里的移动 SkillId

        CooldownStoreSecV2 Store => cooldownHub != null ? cooldownHub.secStore : null;
        string Key(in MoveCostSpec spec)
        {
            if (!string.IsNullOrEmpty(skillIdOverride))
                return skillIdOverride;
            if (!string.IsNullOrWhiteSpace(spec.skillId))
                return spec.skillId.Trim();
            return MoveProfileRules.DefaultSkillId;
        }

        Unit ResolveUnit(Unit unit)
        {
            if (unit != null) return unit;
            var driver = GetComponentInParent<HexBoardTestDriver>();
            return driver != null ? driver.UnitRef : null;
        }

        public bool IsOnCooldown(Unit unit, in MoveCostSpec spec)
        {
            if (turnManager != null)
                return false;
            string key = Key(spec);
            return Store != null && !Store.Ready(key);
        }

        public bool HasEnough(Unit unit, in MoveCostSpec spec)
        {
            int need = Mathf.Max(0, spec.energyPerSecond);
            if (turnManager != null)
            {
                unit = ResolveUnit(unit);
                var pool = unit != null ? turnManager.GetResources(unit) : null;
                if (pool != null)
                    return pool.Has("Energy", need);
            }

            var sourceStats = stats != null ? stats : (ctx != null ? ctx.stats : null);
            if (sourceStats == null) return true;
            return sourceStats.Energy >= need;
        }

        public void Pay(Unit unit, in MoveCostSpec spec)
        {
            if (turnManager != null)
                return;
            int need = Mathf.Max(0, spec.energyPerSecond);
            string key = Key(spec);

            if (stats != null)
                stats.Energy = Mathf.Clamp(stats.Energy - need, 0, stats.MaxEnergy);

            // 开冷却（秒→回合；Move 通常 0）
            if (Store != null && spec.cooldownSeconds > 0f)
            {
                int seconds = Mathf.CeilToInt(spec.cooldownSeconds);
                Store.StartSeconds(key, seconds);
            }
        }
        public void RefundSeconds(Unit unit, in MoveCostSpec spec, int seconds)
        {
            if (seconds <= 0) return;

            if (turnManager != null)
                return;

            if (stats == null) return;
            int refund = Mathf.Max(0, spec.energyPerSecond) * seconds;
            int before = stats.Energy;
            stats.Energy = Mathf.Clamp(stats.Energy + refund, 0, stats.MaxEnergy);

            Debug.Log($"[MoveCost] Refund {seconds}s => +{refund} Energy ({before}->{stats.Energy})",
                      this);
        }
    }
}

