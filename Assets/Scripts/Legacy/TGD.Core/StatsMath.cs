using UnityEngine;

public static class StatsMath
{
    public const int BaseTurnSeconds = 6;

    // —— 回合 / 步数 ——
    public static int TurnTime(int speedSeconds) => BaseTurnSeconds + Mathf.Max(0, speedSeconds);
    public static int StepsAllowed(int moveRate, int timeCostSeconds)
        => Mathf.Max(0, moveRate) * Mathf.Max(0, timeCostSeconds);

    // —— 暴击（评级→几率）——
    // 原始几率：可 > 1
    public static float CritChanceRaw(float baseChance, int rating, float addPct, float ratingPer1Pct = 30f)
    {
        float fromRating = (ratingPer1Pct > 0f) ? (rating / ratingPer1Pct) * 0.01f : 0f;
        return Mathf.Max(0f, baseChance + fromRating + addPct);
    }
    // 用于掷点：0..1
    public static float CritChanceCapped(float critChanceRaw) => Mathf.Min(1f, Mathf.Max(0f, critChanceRaw));
    // 溢出部分：>0 表示 100% 以外的多余几率
    public static float CritOverflow(float critChanceRaw) => Mathf.Max(0f, critChanceRaw - 1f);

    public static float CritMultiplier(int critDamagePct) => Mathf.Max(1f, critDamagePct / 100f);

    // —— 精通（评级×职业系数→百分比小数），允许 >1（+100% 以上）——
    public static float MasteryValue(float baseP, int rating, float addPct, float baseRatingPer1Pct = 20f, float classCoeff = 1f)
    {
        float per1Pct = baseRatingPer1Pct > 0f ? baseRatingPer1Pct : 1f;
        float fromRating = (rating / per1Pct) * classCoeff * 0.01f; // 评级→百分比小数
        return Mathf.Max(0f, baseP + fromRating + addPct);
    }

    public static int CooldownToRounds(int seconds)
        => (seconds <= 0) ? 0 : (seconds + BaseTurnSeconds - 1) / BaseTurnSeconds;
}
