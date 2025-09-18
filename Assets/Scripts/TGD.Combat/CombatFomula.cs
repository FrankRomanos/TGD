using System;

namespace TGD.Core
{
    /// <summary>
    /// Encapsulates the agreed combat math so both runtime systems and tooling can stay in sync.
    /// </summary>
    public static class CombatFormula
    {
        private const float AttributeDivisor = 15f;
        private const float AttributeNormalization = 100f;

        public struct DamageInput
        {
            /// <summary>Raw damage provided by the skill definition or expression.</summary>
            public float SkillDamage;
            /// <summary>Whether the roll ended up as a critical hit.</summary>
            public bool IsCritical;
            /// <summary>The main offensive attribute after all conversions (Strength or Agility).</summary>
            public float PrimaryAttributeValue;
            /// <summary>Extra damage multiplier coming from skill effects (damageincrease1).</summary>
            public float AdditionalDamageMultiplier;
            /// <summary>Extra damage multiplier coming from situational effects (damageincrease2).</summary>
            public float SituationalDamageMultiplier;
            /// <summary>Total damage reduction on the target (0..1).</summary>
            public float DamageReduction;
            /// <summary>Mitigation coming from armour calculations (already processed for the damage school).</summary>
            public float ArmorMitigationMultiplier;
            /// <summary>Skill level threat multiplier.</summary>
            public float SkillThreatMultiplier;
            /// <summary>Skill level shred multiplier.</summary>
            public float SkillShredMultiplier;
        }

        public struct DamageResult
        {
            public float Damage;
            public float Threat;
            public float Shred;
            public float CritMultiplier;
            public float AttributeMultiplier;
        }

        public static DamageResult CalculateDamage(DamageInput input, Stats attacker)
        {
            if (attacker == null)
                throw new ArgumentNullException(nameof(attacker));

            float critMultiplier = input.IsCritical
                ? 2f + attacker.CritDamage / 100f
                : 1f;

            // Attribute scaling follows (Strength or Agility) / 15 / 100 as requested.
            float attributeMultiplier = ClampNonNegative(input.PrimaryAttributeValue / AttributeDivisor / AttributeNormalization);

            float masteryMultiplier = 1f + ClampNonNegative(attacker.Mastery);
            float statDamageMultiplier = 1f + ClampNonNegative(attacker.DamageIncrease);
            float additionalMultiplier = 1f + ClampNonNegative(input.AdditionalDamageMultiplier);
            float situationalMultiplier = 1f + ClampNonNegative(input.SituationalDamageMultiplier);
            float mitigationMultiplier = 1f - Clamp01(input.DamageReduction);
            float armorMultiplier = ClampNonNegative(input.ArmorMitigationMultiplier);
            if (armorMultiplier <= 0f)
                armorMultiplier = 1f;

            float damage = input.SkillDamage * critMultiplier * attributeMultiplier * masteryMultiplier *
                           statDamageMultiplier * additionalMultiplier * situationalMultiplier *
                           mitigationMultiplier * armorMultiplier;

            damage = ClampNonNegative(damage);

            float threatMultiplier = ClampNonNegative(input.SkillThreatMultiplier) + NormalizePercent(attacker.Threat);
            float shredMultiplier = ClampNonNegative(input.SkillShredMultiplier) + NormalizePercent(attacker.Shred);

            float threat = damage * threatMultiplier;
            float shred = damage * shredMultiplier;

            return new DamageResult
            {
                Damage = damage,
                Threat = ClampNonNegative(threat),
                Shred = ClampNonNegative(shred),
                CritMultiplier = critMultiplier,
                AttributeMultiplier = attributeMultiplier
            };
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static float ClampNonNegative(float value)
        {
            return value < 0f ? 0f : value;
        }

        private static float NormalizePercent(float value)
        {
            if (value > 1f)
                return value / 100f;
            if (value < 0f)
                return 0f;
            return value;
        }
    }
}