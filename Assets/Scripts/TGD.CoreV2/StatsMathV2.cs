using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TGD.CoreV2
{
    public static class StatsMathV2
    {
        public const int BaseTurnSeconds = 6;

        // 时间 / 步数
        public static int TurnTime(int speedSeconds) => BaseTurnSeconds + Mathf.Max(0, speedSeconds);

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

        /// <summary>
        /// 乘法后再加法：MR = baseR * Π(mults) + flatAfter
        /// - baseR: 面板基础
        /// - mults: 战斗中/贴附/地形等百分比（乘法）
        /// - flatAfter: 高贵平加（在乘法之后加）
        /// </summary>
        public static float MR_MultiThenFlat(int baseR, IEnumerable<float> mults, float flatAfter)
        {
            float M = 1f;
            if (mults != null)
            {
                foreach (var m in mults)
                    M *= Mathf.Clamp(m, 0.01f, 100f);
            }
            float mr = baseR * M + Mathf.Max(0f, flatAfter);
            return Mathf.Max(0.01f, mr);
        }

        /// <summary>给“显示/预览”用：某 MR 在 timeSec 秒内可走步数（向下取整）。</summary>
        public static int StepsAllowedF32(float mr, int timeSec)
        {
            return Mathf.Max(0, Mathf.FloorToInt(Mathf.Max(0.01f, mr) * Mathf.Max(1, timeSec)));
        }

        // ====== 为兼容旧调用，保留旧 API，内部走新公式 ======

        /// <summary>
        /// 旧：基础+（与基础相关的百分比）+平加 —— 现在等价为：平加当作“乘后平加”
        /// </summary>
        public static int EffectiveMoveRateFromBase(int baseR, IEnumerable<float> percentMults, int flatAddLegacy)
        {
            float mr = MR_MultiThenFlat(
                Mathf.Max(1, baseR),
                percentMults,
                flatAddLegacy // 旧的“平加”当作高贵平加使用（乘法后再加）
            );
            return Mathf.Max(1, Mathf.FloorToInt(mr + 1e-3f));
        }

        /// <summary>旧：给“显示/预览”用的步数计算（int 版），内部转 float。</summary>
        public static int StepsAllowed(int mrInt, int timeSec)
        {
            return StepsAllowedF32(Mathf.Max(1, mrInt), timeSec);
        }
    }
}
