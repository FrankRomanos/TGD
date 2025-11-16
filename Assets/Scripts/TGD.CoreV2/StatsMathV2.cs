using System.Collections.Generic;
using UnityEngine;
using TGD.CoreV2.Rules;

namespace TGD.CoreV2
{
    public static class StatsMathV2
    {
        public const int BaseTurnSeconds = 6;


        public static int TurnTime(int speedSeconds)
        {
            int turnSeconds = BaseTurnSeconds + speedSeconds;
            return Mathf.Max(0, turnSeconds);
        }

        public static float CritChanceRaw(float baseChance, int rating, float addPct, float ratingPer1Pct = 30f)
        {
            float fromRating = (ratingPer1Pct > 0f) ? (rating / ratingPer1Pct) * 0.01f : 0f;
            return Mathf.Max(0f, baseChance + fromRating + addPct);
        }
        public static float CritChanceCapped(float raw) => Mathf.Min(1f, Mathf.Max(0f, raw));
        public static float CritOverflow(float raw) => Mathf.Max(0f, raw - 1f);
        public static float CritMultiplier(int critDamagePct) => Mathf.Max(1f, critDamagePct / 100f);

        public static float MasteryValue(float baseP, int rating, float addPct, float baseRatingPer1Pct = 20f, float classCoeff = 1f)
        {
            float per1Pct = baseRatingPer1Pct > 0f ? baseRatingPer1Pct : 1f;
            float fromRating = (rating / per1Pct) * classCoeff * 0.01f;
            return Mathf.Max(0f, baseP + fromRating + addPct);
        }


        public static float ArmorDR(float armor, float threshold = 200f, float cap = 0.80f, float k2 = 160f)
        {
            return ArmorRules.CalcPhysicalDR(armor);
        }


        public static int CooldownToTurns(int seconds)
            => (seconds <= 0) ? 0 : (seconds + BaseTurnSeconds - 1) / BaseTurnSeconds;

        public static float MR_MultiThenFlat(int baseR, IEnumerable<float> mults, float flatAfter, float min = MoveRateRules.DefaultMin, float max = MoveRateRules.DefaultMax)
        {
            float M = 1f;
            if (mults != null)
            {
                foreach (var m in mults)
                    M *= Mathf.Clamp(m, 0.01f, 100f);
            }

            int minInt = Mathf.FloorToInt(min);
            int maxInt = Mathf.CeilToInt(max);
            int baseClamped = Mathf.Clamp(baseR, minInt, maxInt);

            float mr = baseClamped * M + flatAfter;
            return Mathf.Clamp(mr, min, max);
        }


        public static int StepsAllowedF32(float mr, int timeSec)
        {
            return Mathf.Max(0, Mathf.FloorToInt(Mathf.Max(0.01f, mr) * Mathf.Max(1, timeSec)));
        }


        public static int EffectiveMoveRateFromBase(int baseR, IEnumerable<float> percentMults, int flatAddLegacy, int min = MoveRateRules.DefaultMinInt, int max = MoveRateRules.DefaultMaxInt)
        {
            float mr = MR_MultiThenFlat(
                baseR,
                percentMults,
                flatAddLegacy,
                min,
                max
            );
            int floored = Mathf.FloorToInt(mr + 1e-3f);
            return Mathf.Clamp(floored, min, max);
        }


        public static int StepsAllowed(int mrInt, int timeSec)
        {
            return StepsAllowedF32(Mathf.Max(1, mrInt), timeSec);
        }
    }
}
