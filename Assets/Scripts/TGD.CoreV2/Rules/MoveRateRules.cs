using UnityEngine;

namespace TGD.CoreV2
{
    /// <summary>
    /// Centralized move-rate rule set used by combat systems.
    /// Provides default clamps while allowing unit data to override the bounds.
    /// </summary>
    public static class MoveRateRules
    {
        public const int DefaultMinInt = 1;
        public const int DefaultMaxInt = 14;
        public const float DefaultMin = DefaultMinInt;
        public const float DefaultMax = DefaultMaxInt;

        public static int ResolveMin(StatsV2 stats)
        {
            if (stats == null) return DefaultMinInt;
            return Mathf.Clamp(stats.MoveRateMin, DefaultMinInt, DefaultMaxInt);
        }

        public static int ResolveMax(StatsV2 stats)
        {
            if (stats == null) return DefaultMaxInt;
            int min = ResolveMin(stats);
            return Mathf.Clamp(stats.MoveRateMax, min, DefaultMaxInt);
        }

        public static float Clamp(float value, StatsV2 stats)
        {
            return Mathf.Clamp(value, ResolveMin(stats), ResolveMax(stats));
        }

        public static int Clamp(int value, StatsV2 stats)
        {
            int min = ResolveMin(stats);
            int max = ResolveMax(stats);
            return Mathf.Clamp(value, min, max);
        }

        public static float Clamp(float value, int min, int max)
        {
            return Mathf.Clamp(value, min, max);
        }

        public static int Clamp(int value, int min, int max)
        {
            return Mathf.Clamp(value, min, max);
        }
    }
}
