using UnityEngine;

namespace TGD.CoreV2
{
    public static class StatsMathV2
    {
        public const int BaseTurnSeconds = 6;

        // ʱ�� / ����
        public static int TurnTime(int speedSeconds) => BaseTurnSeconds + Mathf.Max(0, speedSeconds);
        public static int StepsAllowed(int effectiveMoveRate, int timeCostSeconds)
            => Mathf.Max(0, effectiveMoveRate) * Mathf.Max(0, timeCostSeconds);

        // ����
        public static float CritChanceRaw(float baseChance, int rating, float addPct, float ratingPer1Pct = 30f)
        {
            float fromRating = (ratingPer1Pct > 0f) ? (rating / ratingPer1Pct) * 0.01f : 0f;
            return Mathf.Max(0f, baseChance + fromRating + addPct);
        }
        public static float CritChanceCapped(float raw) => Mathf.Min(1f, Mathf.Max(0f, raw));
        public static float CritOverflow(float raw) => Mathf.Max(0f, raw - 1f);
        public static float CritMultiplier(int critDamagePct) => Mathf.Max(1f, critDamagePct / 100f);

        // ��ͨ������ >1��
        public static float MasteryValue(float baseP, int rating, float addPct, float baseRatingPer1Pct = 20f, float classCoeff = 1f)
        {
            float per1Pct = baseRatingPer1Pct > 0f ? baseRatingPer1Pct : 1f;
            float fromRating = (rating / per1Pct) * classCoeff * 0.01f;
            return Mathf.Max(0f, baseP + fromRating + addPct);
        }

        // ���� DR��ռλ���ɰ����Ͷ��ײ�����
        public static float ArmorDR(float armor, float threshold = 200f, float cap = 0.80f, float k2 = 160f)
        {
            if (armor <= 0f) return 0f;
            if (armor <= threshold) return Mathf.Clamp01(armor * 0.0015f);
            float drAtT = Mathf.Clamp01(threshold * 0.0015f);
            float rest = 1f - drAtT;
            float inc = 1f - Mathf.Exp(-(armor - threshold) / Mathf.Max(1f, k2));
            return Mathf.Clamp(drAtT + rest * inc, 0f, cap);
        }

        // ����غ�
        public static int CooldownToTurns(int seconds)
            => (seconds <= 0) ? 0 : (seconds + BaseTurnSeconds - 1) / BaseTurnSeconds;

        // ���� ���ٹ��� ����

        // �ѱ������ɡ��ӵ� base �ϵļӷ���������������Чֵ��1��ֻ�᲻�룩
        public static int EnvAddFromMultiplier(int plannedBaseMoveRate, float mult)
        {
            int baseR = Mathf.Max(1, plannedBaseMoveRate);
            float eff = baseR * Mathf.Clamp(mult, 0.1f, 5f);
            int effInt = Mathf.Max(1, Mathf.FloorToInt(eff));
            return effInt - baseR; // �����ɸ�������>0������<0��
        }

        // �����ɼӷ����ӵ� base �ϵõ���Ч���٣���1��
        public static int MoveRateWithAdds(int baseMoveRate, int addSum)
        {
            return Mathf.Max(1, baseMoveRate + addSum);
        }

    }
}
