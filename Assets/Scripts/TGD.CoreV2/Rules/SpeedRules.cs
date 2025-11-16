using UnityEngine;

namespace TGD.CoreV2.Rules
{
    /// <summary>
    /// Speed rating → seconds conversion utilities.
    /// </summary>
    public static class SpeedRules
    {
        /// <summary>
        /// Base turn seconds (legacy systems still assume 6f per turn).
        /// </summary>
        public const float DefaultBaseTurnSeconds = 6f;

        /// <summary>
        /// Authoring precision multiplier (designs enter rating * 10 to gain 0.1 resolution).
        /// </summary>
        public const float DefaultRatingPrecision = 10f;

        /// <summary>
        /// Expected upper bound of total rating that can be stacked from gear/blueprints.
        /// </summary>
        public const float DefaultRatingMax = 12f;

        /// <summary>
        /// Maximum seconds gained purely from rating (beyond the base turn).
        /// </summary>
        public const float DefaultSecondsMaxFromRating = 6f;

        /// <summary>
        /// 0 &lt; p &lt; 1 => diminishing returns curve.
        /// </summary>
        public const float DefaultCurveExponent = 0.8f;

        /// <summary>
        /// Maps the blueprint/gear rating (int) to a float amount of extra seconds per turn.
        /// </summary>
        public static float MapRatingToSeconds(float rating)
        {
            if (rating <= 0f)
                return 0f;

            float clamped = Mathf.Min(rating, DefaultRatingMax);
            float ratio = clamped / Mathf.Max(1f, DefaultRatingMax);
            return DefaultSecondsMaxFromRating * Mathf.Pow(ratio, DefaultCurveExponent);
        }

        /// <summary>
        /// Converts the serialized blueprint value (scaled by <see cref="DefaultRatingPrecision"/>) back to curve units.
        /// </summary>
        public static float DecodeBlueprintRating(int serializedValue)
        {
            if (serializedValue <= 0)
                return 0f;
            return serializedValue / DefaultRatingPrecision;
        }

        /// <summary>
        /// Integer seconds gained from rating (flooring: "只舍不入").
        /// </summary>
        public static int MapRatingToSecondsInt(float rating)
        {
            float seconds = MapRatingToSeconds(rating);
            if (seconds <= 0f)
                return 0;
            return Mathf.Max(0, Mathf.FloorToInt(seconds));
        }
    }
}

