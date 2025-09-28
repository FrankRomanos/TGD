using UnityEngine;

public static class StatsMath
{
    public const int BaseTurnSeconds = 6;

    // ���� �غ� / ���� ����
    public static int TurnTime(int speedSeconds) => BaseTurnSeconds + Mathf.Max(0, speedSeconds);
    public static int StepsAllowed(int moveRate, int timeCostSeconds)
        => Mathf.Max(0, moveRate) * Mathf.Max(0, timeCostSeconds);

    // ���� ���������������ʣ�����
    // ԭʼ���ʣ��� > 1
    public static float CritChanceRaw(float baseChance, int rating, float addPct, float ratingPer1Pct = 30f)
    {
        float fromRating = (ratingPer1Pct > 0f) ? (rating / ratingPer1Pct) * 0.01f : 0f;
        return Mathf.Max(0f, baseChance + fromRating + addPct);
    }
    // �������㣺0..1
    public static float CritChanceCapped(float critChanceRaw) => Mathf.Min(1f, Mathf.Max(0f, critChanceRaw));
    // ������֣�>0 ��ʾ 100% ����Ķ��༸��
    public static float CritOverflow(float critChanceRaw) => Mathf.Max(0f, critChanceRaw - 1f);

    public static float CritMultiplier(int critDamagePct) => Mathf.Max(1f, critDamagePct / 100f);

    // ���� ��ͨ��������ְҵϵ�����ٷֱ�С���������� >1��+100% ���ϣ�����
    public static float MasteryValue(float baseP, int rating, float addPct, float baseRatingPer1Pct = 20f, float classCoeff = 1f)
    {
        float per1Pct = baseRatingPer1Pct > 0f ? baseRatingPer1Pct : 1f;
        float fromRating = (rating / per1Pct) * classCoeff * 0.01f; // �������ٷֱ�С��
        return Mathf.Max(0f, baseP + fromRating + addPct);
    }

    public static int CooldownToRounds(int seconds)
        => (seconds <= 0) ? 0 : (seconds + BaseTurnSeconds - 1) / BaseTurnSeconds;
}
