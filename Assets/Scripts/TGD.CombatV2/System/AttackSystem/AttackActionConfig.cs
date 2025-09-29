// File: TGD.CombatV2/AttackActionConfigV2.cs
using UnityEngine;

namespace TGD.CombatV2
{
    [CreateAssetMenu(menuName = "TGD/CombatV2/Attack Action Config")]
    public class AttackActionConfigV2 : ScriptableObject
    {
        [Header("Timing / Budget")]
        [Tooltip("本次攻击可用于【位移】的时间预算（秒，取整）")]
        public int baseTimeSeconds = 2;               // 例：2 / 3 / 1

        [Tooltip("加速累计节省 ≥ 阈值返还 1s")]
        [Range(0.1f, 1.0f)] public float refundThresholdSeconds = 0.8f;

        [Header("Energy Cost")]
        [Tooltip("基础能量消耗（例：2s=20，3s=30，1s=5）")]
        public int baseEnergyCost = 20;

        [Tooltip("本回合第二次起每次 +50% 基础消耗；可关闭")]
        public bool applySameTurnPenalty = true;

        [Tooltip("同回合叠加系数（默认 0.5 = +50%/次）")]
        [Range(0f, 1f)] public float sameTurnPenaltyRate = 0.5f;

        [Header("Reach")]
        [Tooltip("近战格距（1=相邻）")]
        public int meleeRange = 1;

        [Header("Facing")]
        public float keepDeg = 45f;
        public float turnDeg = 135f;
        public float turnSpeedDegPerSec = 720f;
    }
}
