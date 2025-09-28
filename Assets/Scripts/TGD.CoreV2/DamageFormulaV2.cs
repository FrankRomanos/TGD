using UnityEngine;

namespace TGD.CoreV2
{
    public static class DamageFormulaV2
    {
        /// <summary>
        /// ͳһ�˺��� (Atk*Coeff) * Crit * (1+Primary) * (1+Mastery?) * (1+A) * (1+B) * (1+C) * (1 - [Armor + RedA + RedB + RedC])
        /// ���� [Armor + Red...] �� clamp �� [0,0.95]��
        /// </summary>
        public static int ComputeDamage(ref bool isCrit,
                                        StatsV2 atk, StatsV2 def,
                                        float skillCoeff,
                                        bool includeMasteryBucket,
                                        float extraCritDamageFromOverflow = 0f)
        {
            // ������
            float baseDmg = Mathf.Max(0, atk.Attack) * Mathf.Max(0f, skillCoeff);

            // ����
            float critMult = 1f;
            if (isCrit)
            {
                critMult = atk.CritMult * (1f + Mathf.Max(0f, extraCritDamageFromOverflow)); // ���ת����
            }

            // ��������Ͱ���˷���
            float amp = 1f;
            amp *= (1f + Mathf.Max(0f, atk.PrimaryP));
            if (includeMasteryBucket) amp *= (1f + Mathf.Max(0f, atk.Mastery));
            amp *= (1f + Mathf.Max(0f, atk.DmgBonusA_P));
            amp *= (1f + Mathf.Max(0f, atk.DmgBonusB_P));
            amp *= (1f + Mathf.Max(0f, atk.DmgBonusC_P));

            float afterAmp = baseDmg * critMult * amp;

            // ���ˣ��ӷ� + Clamp��
            float armorDR = StatsMathV2.ArmorDR(def.Armor);
            float totalDR = Mathf.Clamp01(armorDR + Mathf.Max(0f, def.ReduceA_P) + Mathf.Max(0f, def.ReduceB_P) + Mathf.Max(0f, def.ReduceC_P));
            totalDR = Mathf.Min(totalDR, 0.95f);

            float final = afterAmp * (1f - totalDR);

            return Mathf.Max(0, Mathf.RoundToInt(final));
        }

        public static float ComputeThreat(float finalDamage, float skillThreatScale, StatsV2 atk)
        {
            return Mathf.Max(0f, finalDamage) * Mathf.Max(0f, skillThreatScale) * (1f + Mathf.Max(0f, atk.ThreatAddPct));
        }

        public static float ComputeShred(float finalDamage, float skillShredScale, StatsV2 atk)
        {
            return Mathf.Max(0f, finalDamage) * Mathf.Max(0f, skillShredScale) * (1f + Mathf.Max(0f, atk.ShredAddPct));
        }
    }
}

