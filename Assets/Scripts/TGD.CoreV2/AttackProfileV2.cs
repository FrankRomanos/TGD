using System;
using UnityEngine;

namespace TGD.CoreV2
{
    /// <summary>
    /// Serialized attack pacing values copied from blueprint data.
    /// </summary>
    [Serializable]
    public sealed class AttackProfileV2
    {
        [Tooltip("Base attack execution time in seconds (integer).")]
        [Min(AttackProfileRules.MinSeconds)]
        public int attackSeconds = AttackProfileRules.DefaultSeconds;

        [Tooltip("Base energy cost for the standard attack.")]
        [Min(0)]
        public int energyCost = AttackProfileRules.DefaultEnergyCost;

        [Tooltip("Accumulated saved time threshold before refunding 1 second of approach.")]
        [Range(0.01f, 1f)]
        public float refundThresholdSeconds = AttackProfileRules.DefaultRefundThresholdSeconds;

        [Tooltip("Cutoff duration for melee free move refunds (seconds).")]
        [Min(0f)]
        public float freeMoveCutoffSeconds = AttackProfileRules.DefaultFreeMoveCutoffSeconds;

        [Tooltip("Melee engagement range in hexes.")]
        [Min(AttackProfileRules.MinMeleeRange)]
        public int meleeRange = AttackProfileRules.DefaultMeleeRange;

        [Tooltip("Facing change preserved without turning (degrees).")]
        [Range(0f, 360f)]
        public float keepDeg = AttackProfileRules.DefaultKeepDeg;

        [Tooltip("Facing change that triggers a turn animation (degrees).")]
        [Range(0f, 360f)]
        public float turnDeg = AttackProfileRules.DefaultTurnDeg;

        [Tooltip("Turn speed in degrees per second.")]
        [Min(0f)]
        public float turnSpeedDegPerSec = AttackProfileRules.DefaultTurnSpeedDegPerSec;
        public const string DefaultSkillId = "Attack";
        public void CopyFrom(AttackProfileV2 source)
        {
            if (source == null)
            {
                attackSeconds = AttackProfileRules.DefaultSeconds;
                energyCost = AttackProfileRules.DefaultEnergyCost;
                return;
            }

            attackSeconds = source.attackSeconds;
            energyCost = source.energyCost;
            refundThresholdSeconds = source.refundThresholdSeconds;
            freeMoveCutoffSeconds = source.freeMoveCutoffSeconds;
            meleeRange = source.meleeRange;
            keepDeg = source.keepDeg;
            turnDeg = source.turnDeg;
            turnSpeedDegPerSec = source.turnSpeedDegPerSec;
            Clamp();
        }

        public void Clamp()
        {
            attackSeconds = Mathf.Clamp(attackSeconds, AttackProfileRules.MinSeconds, AttackProfileRules.MaxSeconds);
            energyCost = Mathf.Clamp(energyCost, 0, AttackProfileRules.MaxEnergyCost);
            refundThresholdSeconds = Mathf.Clamp(refundThresholdSeconds, 0.01f, 1f);
            freeMoveCutoffSeconds = Mathf.Max(0f, freeMoveCutoffSeconds);
            meleeRange = Mathf.Clamp(meleeRange, AttackProfileRules.MinMeleeRange, AttackProfileRules.MaxMeleeRange);
            keepDeg = Mathf.Repeat(Mathf.Max(0f, keepDeg), 360f);
            turnDeg = Mathf.Repeat(Mathf.Max(0f, turnDeg), 360f);
            turnSpeedDegPerSec = Mathf.Max(0f, turnSpeedDegPerSec);
        }
    }

    public static class AttackProfileRules
    {
        public const int MinSeconds = 1;
        public const int MaxSeconds = 12;
        public const int DefaultSeconds = 2;
        public const int DefaultEnergyCost = 20;
        public const int MaxEnergyCost = 9999;
        public const string DefaultSkillId = "Attack";
        public const float DefaultRefundThresholdSeconds = 0.8f;
        public const float DefaultFreeMoveCutoffSeconds = 0.2f;
        public const int MinMeleeRange = 1;
        public const int MaxMeleeRange = 6;
        public const int DefaultMeleeRange = 1;
        public const float DefaultKeepDeg = 45f;
        public const float DefaultTurnDeg = 135f;
        public const float DefaultTurnSpeedDegPerSec = 720f;

        public static int ResolveSeconds(StatsV2 stats)
        {
            if (stats?.AttackProfile == null)
                return DefaultSeconds;
            return Mathf.Clamp(stats.AttackProfile.attackSeconds, MinSeconds, MaxSeconds);
        }

        public static int ResolveEnergy(StatsV2 stats)
        {
            if (stats?.AttackProfile == null)
                return DefaultEnergyCost;
            return Mathf.Clamp(stats.AttackProfile.energyCost, 0, MaxEnergyCost);
        }

        public static float ResolveRefundThreshold(StatsV2 stats)
        {
            if (stats?.AttackProfile == null)
                return DefaultRefundThresholdSeconds;
            return Mathf.Clamp(stats.AttackProfile.refundThresholdSeconds, 0.01f, 1f);
        }

        public static float ResolveFreeMoveCutoff(StatsV2 stats)
        {
            if (stats?.AttackProfile == null)
                return DefaultFreeMoveCutoffSeconds;
            return Mathf.Max(0f, stats.AttackProfile.freeMoveCutoffSeconds);
        }

        public static int ResolveMeleeRange(StatsV2 stats)
        {
            if (stats?.AttackProfile == null)
                return DefaultMeleeRange;
            return Mathf.Clamp(stats.AttackProfile.meleeRange, MinMeleeRange, MaxMeleeRange);
        }

        public static float ResolveKeepDeg(StatsV2 stats)
        {
            if (stats?.AttackProfile == null)
                return DefaultKeepDeg;
            return Mathf.Repeat(Mathf.Max(0f, stats.AttackProfile.keepDeg), 360f);
        }

        public static float ResolveTurnDeg(StatsV2 stats)
        {
            if (stats?.AttackProfile == null)
                return DefaultTurnDeg;
            return Mathf.Repeat(Mathf.Max(0f, stats.AttackProfile.turnDeg), 360f);
        }

        public static float ResolveTurnSpeed(StatsV2 stats)
        {
            if (stats?.AttackProfile == null)
                return DefaultTurnSpeedDegPerSec;
            return Mathf.Max(0f, stats.AttackProfile.turnSpeedDegPerSec);
        }
    }
}
