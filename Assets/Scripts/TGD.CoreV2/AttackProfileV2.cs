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
            Clamp();
        }

        public void Clamp()
        {
            attackSeconds = Mathf.Clamp(attackSeconds, AttackProfileRules.MinSeconds, AttackProfileRules.MaxSeconds);
            energyCost = Mathf.Clamp(energyCost, 0, AttackProfileRules.MaxEnergyCost);
        }
    }

    public static class AttackProfileRules
    {
        public const int MinSeconds = 1;
        public const int MaxSeconds = 12;
        public const int DefaultSeconds = 2;
        public const int DefaultEnergyCost = 20;
        public const int MaxEnergyCost = 9999;

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
    }
}
