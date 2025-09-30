// File: TGD.CombatV2/AttackActionConfigV2.cs
using UnityEngine;

namespace TGD.CombatV2
{
    [CreateAssetMenu(menuName = "TGD/CombatV2/Attack Action Config")]
    public class AttackActionConfigV2 : ScriptableObject
    {
        [Header("Timing / Budget")]
        [Tooltip("ιڡλơʱԤ㣨룬ȡ")]
        public int baseTimeSeconds = 2;               // 2 / 3 / 1

        [Tooltip("攻击动作本体耗时（秒），用于确认阶段的时间预算与扣费。")]
        public float timeCostSeconds = 1f;

        [Tooltip("ۼƽʡ  ֵ 1s")]
        [Range(0.1f, 1.0f)] public float refundThresholdSeconds = 0.8f;

        [Header("Energy Cost")]
        [Tooltip("ģ2s=203s=301s=5")]
        public int baseEnergyCost = 20;

        [Tooltip("غϵڶÿ +50% ģɹر")]
        public bool applySameTurnPenalty = true;

        [Tooltip("ͬغϵϵĬ 0.5 = +50%/Σ")]
        [Range(0f, 1f)] public float sameTurnPenaltyRate = 0.5f;

        [Header("Reach")]
        [Tooltip("սࣨ1=ڣ")]
        public int meleeRange = 1;

        [Header("Facing")]
        public float keepDeg = 45f;
        public float turnDeg = 135f;
        public float turnSpeedDegPerSec = 720f;

        public float AttackTimeSeconds
        {
            get
            {
                if (timeCostSeconds > 0f) return timeCostSeconds;
                return Mathf.Max(0f, baseTimeSeconds);
            }
        }

        void OnValidate()
        {
            if (timeCostSeconds <= 0f)
                timeCostSeconds = Mathf.Max(0f, baseTimeSeconds);
        }
    }
}
