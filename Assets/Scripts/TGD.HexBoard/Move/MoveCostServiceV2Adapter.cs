using UnityEngine;
using TGD.CoreV2; // ���� CoreV2��HexBoard.asmdef ����Ӷ� CoreV2.asmdef �����ã�

namespace TGD.HexBoard
{
    /// <summary>
    /// �� HexClickMover ͨ�� IMoveCostService ���� StatsV2 + CooldownStoreV2��
    /// </summary>
    public sealed class MoveCostServiceV2Adapter : MonoBehaviour, IMoveCostService
    {
        [Header("Refs")]
        public StatsV2 stats;              // ����λ�� CoreV2 ���
        public CooldownHubV2 cooldownHub;  // ������ͬ����/ͬ�������һ��

        [Header("Options")]
        public string actionIdOverride = ""; // �������� MoveActionConfig.actionId

        CooldownStoreV2 Store => cooldownHub != null ? cooldownHub.store : null;
        string Key(MoveActionConfig cfg)
            => string.IsNullOrEmpty(actionIdOverride) ? (cfg != null ? cfg.actionId : "Move") : actionIdOverride;

        public bool IsOnCooldown(Unit unit, MoveActionConfig cfg)
            => Store != null && Store.RoundsLeft(Key(cfg)) > 0;

        public bool HasEnough(Unit unit, MoveActionConfig cfg)
        {
            if (stats == null || cfg == null) return true; // δ��������У����ڲ���
            return stats.Energy >= cfg.energyCost;
        }

        public void Pay(Unit unit, MoveActionConfig cfg)
        {
            if (stats == null || cfg == null) return;

            // ������
            stats.Energy = Mathf.Clamp(stats.Energy - cfg.energyCost, 0, stats.MaxEnergy);

            // ����ȴ������֣�Move ͨ�� 0��
            if (Store != null && cfg.cooldownSeconds > 0f)
            {
                int rounds = StatsMathV2.CooldownToRounds(Mathf.CeilToInt(cfg.cooldownSeconds));
                Store.Start(Key(cfg), rounds);
            }
        }
    }
}

