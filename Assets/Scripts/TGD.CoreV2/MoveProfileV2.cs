using System;
using UnityEngine;

namespace TGD.CoreV2
{
    /// <summary>
    /// Serialized movement pacing values copied from blueprint data.
    /// </summary>
    [Serializable]
    public sealed class MoveProfileV2
    {
        [Tooltip("Baseline seconds reserved when planning movement (ceil to int).")]
        [Min(MoveProfileRules.MinSeconds)]
        public float baseTimeSeconds = MoveProfileRules.DefaultSeconds;

        [Tooltip("Energy cost per second of movement.")]
        [Min(0)]
        public int energyPerSecond = MoveProfileRules.DefaultEnergyPerSecond;

        [Tooltip("Cooldown applied to the move action when resolved outside TMV2 (seconds).")]
        [Min(0f)]
        public float cooldownSeconds = MoveProfileRules.DefaultCooldownSeconds;

        [Tooltip("Fallback steps shown when no path preview is available.")]
        [Min(MoveProfileRules.MinFallbackSteps)]
        public int fallbackSteps = MoveProfileRules.DefaultFallbackSteps;

        [Tooltip("Cap on steps allowed during a manual move preview.")]
        [Min(MoveProfileRules.MinStepsCap)]
        public int stepsCap = MoveProfileRules.DefaultStepsCap;

        [Tooltip("Accumulated saved time threshold before refunding 1 second.")]
        [Range(0.01f, 1f)]
        public float refundThresholdSeconds = MoveProfileRules.DefaultRefundThresholdSeconds;

        [Tooltip("Action identifier used when reporting move costs to resource systems.")]
        public string actionId = MoveProfileRules.DefaultActionId;

        [Tooltip("Facing change preserved without turning (degrees).")]
        [Range(0f, 360f)]
        public float keepDeg = MoveProfileRules.DefaultKeepDeg;

        [Tooltip("Facing change that triggers a turn animation (degrees).")]
        [Range(0f, 360f)]
        public float turnDeg = MoveProfileRules.DefaultTurnDeg;

        [Tooltip("Turn speed in degrees per second.")]
        [Min(0f)]
        public float turnSpeedDegPerSec = MoveProfileRules.DefaultTurnSpeedDegPerSec;

        public void CopyFrom(MoveProfileV2 source)
        {
            if (source == null)
            {
                baseTimeSeconds = MoveProfileRules.DefaultSeconds;
                energyPerSecond = MoveProfileRules.DefaultEnergyPerSecond;
                cooldownSeconds = MoveProfileRules.DefaultCooldownSeconds;
                fallbackSteps = MoveProfileRules.DefaultFallbackSteps;
                stepsCap = MoveProfileRules.DefaultStepsCap;
                refundThresholdSeconds = MoveProfileRules.DefaultRefundThresholdSeconds;
                actionId = MoveProfileRules.DefaultActionId;
                keepDeg = MoveProfileRules.DefaultKeepDeg;
                turnDeg = MoveProfileRules.DefaultTurnDeg;
                turnSpeedDegPerSec = MoveProfileRules.DefaultTurnSpeedDegPerSec;
                return;
            }

            baseTimeSeconds = source.baseTimeSeconds;
            energyPerSecond = source.energyPerSecond;
            cooldownSeconds = source.cooldownSeconds;
            fallbackSteps = source.fallbackSteps;
            stepsCap = source.stepsCap;
            refundThresholdSeconds = source.refundThresholdSeconds;
            actionId = source.actionId;
            keepDeg = source.keepDeg;
            turnDeg = source.turnDeg;
            turnSpeedDegPerSec = source.turnSpeedDegPerSec;
            Clamp();
        }

        public void Clamp()
        {
            baseTimeSeconds = Mathf.Clamp(baseTimeSeconds, MoveProfileRules.MinSeconds, MoveProfileRules.MaxSeconds);
            energyPerSecond = Mathf.Clamp(energyPerSecond, 0, MoveProfileRules.MaxEnergyPerSecond);
            cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
            fallbackSteps = Mathf.Clamp(fallbackSteps, MoveProfileRules.MinFallbackSteps, MoveProfileRules.MaxFallbackSteps);
            stepsCap = Mathf.Clamp(stepsCap, MoveProfileRules.MinStepsCap, MoveProfileRules.MaxStepsCap);
            refundThresholdSeconds = Mathf.Clamp(refundThresholdSeconds, 0.01f, 1f);
            keepDeg = Mathf.Repeat(Mathf.Max(0f, keepDeg), 360f);
            turnDeg = Mathf.Repeat(Mathf.Max(0f, turnDeg), 360f);
            turnSpeedDegPerSec = Mathf.Max(0f, turnSpeedDegPerSec);
            if (string.IsNullOrWhiteSpace(actionId))
                actionId = MoveProfileRules.DefaultActionId;
        }
    }

    public static class MoveProfileRules
    {
        public const float MinSeconds = 0.1f;
        public const float MaxSeconds = 12f;
        public const float DefaultSeconds = 1f;

        public const int DefaultEnergyPerSecond = 10;
        public const int MaxEnergyPerSecond = 9999;

        public const float DefaultCooldownSeconds = 0f;

        public const int MinFallbackSteps = 1;
        public const int MaxFallbackSteps = 24;
        public const int DefaultFallbackSteps = 3;

        public const int MinStepsCap = 1;
        public const int MaxStepsCap = 48;
        public const int DefaultStepsCap = 12;

        public const float DefaultRefundThresholdSeconds = 0.8f;

        public const string DefaultActionId = "Move";

        public const float DefaultKeepDeg = 45f;
        public const float DefaultTurnDeg = 135f;
        public const float DefaultTurnSpeedDegPerSec = 720f;

        public static float ResolveBaseSeconds(StatsV2 stats)
        {
            if (stats?.MoveProfile == null)
                return DefaultSeconds;
            return Mathf.Clamp(stats.MoveProfile.baseTimeSeconds, MinSeconds, MaxSeconds);
        }

        public static int ResolveEnergyPerSecond(StatsV2 stats)
        {
            if (stats?.MoveProfile == null)
                return DefaultEnergyPerSecond;
            return Mathf.Clamp(stats.MoveProfile.energyPerSecond, 0, MaxEnergyPerSecond);
        }

        public static float ResolveCooldownSeconds(StatsV2 stats)
        {
            if (stats?.MoveProfile == null)
                return DefaultCooldownSeconds;
            return Mathf.Max(0f, stats.MoveProfile.cooldownSeconds);
        }

        public static int ResolveFallbackSteps(StatsV2 stats)
        {
            if (stats?.MoveProfile == null)
                return DefaultFallbackSteps;
            return Mathf.Clamp(stats.MoveProfile.fallbackSteps, MinFallbackSteps, MaxFallbackSteps);
        }

        public static int ResolveStepsCap(StatsV2 stats)
        {
            if (stats?.MoveProfile == null)
                return DefaultStepsCap;
            return Mathf.Clamp(stats.MoveProfile.stepsCap, MinStepsCap, MaxStepsCap);
        }

        public static float ResolveRefundThreshold(StatsV2 stats)
        {
            if (stats?.MoveProfile == null)
                return DefaultRefundThresholdSeconds;
            return Mathf.Clamp(stats.MoveProfile.refundThresholdSeconds, 0.01f, 1f);
        }

        public static string ResolveActionId(StatsV2 stats)
        {
            if (stats?.MoveProfile == null)
                return DefaultActionId;
            var id = stats.MoveProfile.actionId;
            return string.IsNullOrWhiteSpace(id) ? DefaultActionId : id.Trim();
        }

        public static float ResolveKeepDeg(StatsV2 stats)
        {
            if (stats?.MoveProfile == null)
                return DefaultKeepDeg;
            return Mathf.Repeat(Mathf.Max(0f, stats.MoveProfile.keepDeg), 360f);
        }

        public static float ResolveTurnDeg(StatsV2 stats)
        {
            if (stats?.MoveProfile == null)
                return DefaultTurnDeg;
            return Mathf.Repeat(Mathf.Max(0f, stats.MoveProfile.turnDeg), 360f);
        }

        public static float ResolveTurnSpeed(StatsV2 stats)
        {
            if (stats?.MoveProfile == null)
                return DefaultTurnSpeedDegPerSec;
            return Mathf.Max(0f, stats.MoveProfile.turnSpeedDegPerSec);
        }
    }
}
