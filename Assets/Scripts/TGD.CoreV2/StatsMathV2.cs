using System.Linq;
using UnityEngine;

namespace TGD.CoreV2
{
    public static class StatsMathV2
    {
        public const int BaseTurnSeconds = 6;

        // 时间 / 步数
        public static int TurnTime(int speedSeconds) => BaseTurnSeconds + Mathf.Max(0, speedSeconds);
        public static int StepsAllowed(int effectiveMoveRate, int timeCostSeconds)
            => Mathf.Max(0, effectiveMoveRate) * Mathf.Max(0, timeCostSeconds);

        // 暴击
        public static float CritChanceRaw(float baseChance, int rating, float addPct, float ratingPer1Pct = 30f)
        {
            float fromRating = (ratingPer1Pct > 0f) ? (rating / ratingPer1Pct) * 0.01f : 0f;
            return Mathf.Max(0f, baseChance + fromRating + addPct);
        }
        public static float CritChanceCapped(float raw) => Mathf.Min(1f, Mathf.Max(0f, raw));
        public static float CritOverflow(float raw) => Mathf.Max(0f, raw - 1f);
        public static float CritMultiplier(int critDamagePct) => Mathf.Max(1f, critDamagePct / 100f);

        // 精通（允许 >1）
        public static float MasteryValue(float baseP, int rating, float addPct, float baseRatingPer1Pct = 20f, float classCoeff = 1f)
        {
            float per1Pct = baseRatingPer1Pct > 0f ? baseRatingPer1Pct : 1f;
            float fromRating = (rating / per1Pct) * classCoeff * 0.01f;
            return Mathf.Max(0f, baseP + fromRating + addPct);
        }

        // 护甲 DR（占位，可按类型多套参数）
        public static float ArmorDR(float armor, float threshold = 200f, float cap = 0.80f, float k2 = 160f)
        {
            if (armor <= 0f) return 0f;
            if (armor <= threshold) return Mathf.Clamp01(armor * 0.0015f);
            float drAtT = Mathf.Clamp01(threshold * 0.0015f);
            float rest = 1f - drAtT;
            float inc = 1f - Mathf.Exp(-(armor - threshold) / Mathf.Max(1f, k2));
            return Mathf.Clamp(drAtT + rest * inc, 0f, cap);
        }

        // 秒→回合
        public static int CooldownToTurns(int seconds)
            => (seconds <= 0) ? 0 : (seconds + BaseTurnSeconds - 1) / BaseTurnSeconds;

        // —— 移速工具 ——

        // ===== 移速统一口径（基于“基础移速”只做加减，不连乘）=====

        /// <summary>
        /// 把倍率 m(>0) 转成“基于基础移速 baseR 的增量”：floor(baseR * (m - 1))
        /// 例如 baseR=5, m=2.0 → +5；m=0.2 → -4
        /// </summary>
        public static int EnvAddFromMultiplier(int baseR, float multiplier)
        {
            int b = Mathf.Max(1, baseR);
            if (multiplier <= 0f) return 0;
            float add = b * (multiplier - 1f);
            return Mathf.FloorToInt(add);
        }

        /// <summary>
        /// 以“基础移速 baseR”为参照，收集所有倍率（都相对基础），与平坦加成合并。
        /// 注意：多个倍率的“增量”都基于同一个基础 b 计算，然后再一次性相加，避免连乘。
        /// </summary>


        public static int EffectiveMoveRateFromBase(int baseR, System.Collections.Generic.IEnumerable<float> pctMultipliers, int flatAdd)
        {
            int b = Mathf.Max(1, baseR);
            int sum = flatAdd;
            if (pctMultipliers != null)
            {
                foreach (var m in pctMultipliers)
                    sum += EnvAddFromMultiplier(b, m);
            }
            return Mathf.Max(1, b + sum);
        }
    }
}
