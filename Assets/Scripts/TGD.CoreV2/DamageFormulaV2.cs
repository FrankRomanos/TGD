using UnityEngine;

namespace TGD.CoreV2
{
    public static class DamageFormulaV2
    {
        /// <summary>
        /// Damage formula:
        /// (Atk * skillCoeff) * crit * (1 + Primary) * (1 + Mastery?) * (1 + DamageBonusPct) * (1 - totalReduction).
        /// totalReduction = clamp(Armor + DamageReducePct, [0, 0.95]).
        /// </summary>
        public static int ComputeDamage(ref bool isCrit,
                                        StatsV2 atk, StatsV2 def,
                                        float skillCoeff,
                                        bool includeMasteryBucket,
                                        float extraCritDamageFromOverflow = 0f)
        {
            // Base term
            float baseDmg = Mathf.Max(0, atk.Attack) * Mathf.Max(0f, skillCoeff);

            // Crit multiplier
            float critMult = 1f;
            if (isCrit)
            {
                critMult = atk.CritMult * (1f + Mathf.Max(0f, extraCritDamageFromOverflow));
            }

            // Multiplicative bonuses
            float amp = 1f;
            amp *= (1f + Mathf.Max(0f, atk.PrimaryP));
            if (includeMasteryBucket) amp *= (1f + Mathf.Max(0f, atk.Mastery));
            amp *= (1f + Mathf.Max(0f, atk.DamageBonusPct));

            float afterAmp = baseDmg * critMult * amp;

            // Damage reduction (additive then clamped)
            float armorDR = StatsMathV2.ArmorDR(def.Armor);
            float totalDR = Mathf.Clamp01(armorDR + Mathf.Max(0f, def.DamageReducePct));
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

