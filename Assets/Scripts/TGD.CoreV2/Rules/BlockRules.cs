using UnityEngine;

namespace TGD.CoreV2.Rules
{
    /// <summary>
    /// Fixed block calculations (HP-scaled) granted by reaching specific armor bands.
    /// </summary>
    public static class BlockRules
    {
        public const float ArmorThresholdStart = 140f;
        public const float ArmorThresholdFull = 250f;
        public const float BlockMaxPercentOfHP = 0.09f;

        /// <summary>
        /// Converts armor & max HP into a flat block amount applied after percent-based DR.
        /// </summary>
        public static float CalcFlatBlock(float armor, float maxHP)
        {
            if (armor <= ArmorThresholdStart)
                return 0f;
            if (maxHP <= 0f || BlockMaxPercentOfHP <= 0f)
                return 0f;

            float clampedArmor = Mathf.Clamp(armor, ArmorThresholdStart, ArmorThresholdFull);
            float span = Mathf.Max(1f, ArmorThresholdFull - ArmorThresholdStart);
            float lerp = (clampedArmor - ArmorThresholdStart) / span;
            float blockPct = BlockMaxPercentOfHP * Mathf.Clamp01(lerp);
            return Mathf.Max(0f, maxHP) * blockPct;
        }
    }
}
