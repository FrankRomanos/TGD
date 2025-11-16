using UnityEngine;

namespace TGD.CoreV2.Rules
{
    /// <summary>
    /// Shared configuration + helpers for mapping armor ratings to damage reduction.
    /// </summary>
    public static class ArmorRules
    {
        public const float DRMaxPhys = 0.99f;
        public const float ArmorScaleA = 128.9f;
        public const float ArmorPow = 1.83f;
        public const float ArmorScaleC = 1.0133f;

        /// <summary>
        /// Computes the physical damage reduction provided by armor using the V1 logistic curve.
        /// </summary>
        public static float CalcPhysicalDR(float armor)
        {
            if (armor <= 0f)
                return 0f;

            float t = armor / ArmorScaleA;
            float tPow = Mathf.Pow(t, ArmorPow);
            float raw = ArmorScaleC * tPow / (1f + tPow);
            return Mathf.Clamp(raw, 0f, DRMaxPhys);
        }

        /// <summary>
        /// Armor provides half value versus elemental sources, without participating in block.
        /// </summary>
        public static float CalcElementalDR(float armor)
        {
            float drPhys = CalcPhysicalDR(armor);
            float drElem = 0.5f * drPhys;
            return Mathf.Clamp(drElem, 0f, 0.5f * DRMaxPhys);
        }
    }
}
