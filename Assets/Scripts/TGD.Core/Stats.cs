using System;

namespace TGD.Core
{
    /// <summary>
    /// Aggregates the base stats for a combat unit.
    /// </summary>
    [Serializable]
    public class Stats
    {
        public int Level;

        // Vital resources.
        public int MaxHP;
        public int HP;
        public int Stamina;

        // Base offensive power.
        public int Attack;
        public int SpellPower;

        // Class resources.
        public int Energy;
        public int MaxEnergy;
        public int EnergyRegenPer2s;

        public int Discipline;
        public int MaxDiscipline;

        public int Iron;
        public int MaxIron;

        public int Rage;
        public int MaxRage;

        public int Versatility;
        public int MaxVersatility;

        public int Gunpowder;
        public int MaxGunpowder;

        public int Point;
        public int MaxPoint;

        public int Combo;
        public int MaxCombo;

        public int Punch;
        public int MaxPunch;

        public int Qi;
        public int MaxQi;

        public int Vision;
        public int MaxVision;

        // Posture (class exclusive resource / mastery extension).
        public int Posture;
        public int MaxPosture;
        public int Strength;
        public int Agility;
        // General damage amplification from gear / buffs (multiplicative with other sources,
        // stacks additively only when coming from the same skill ID).
        public float DamageIncrease;
        public float HealIncrease;
        public float DamageIncreaseSecondary;
        public float DamageReduction;
        public float DamageTakenMultiplier = 1f;

        // Defensive layer.
        public int Armor;
        public int ArmorPenetration;

        // Ratings converted to normalized ratios. Example: 0.325f => 32.5% crit chance.
        public float Crit;
        public float CritDamage = 200f; // Stored as percentage bonus. 200 => +200% crit damage.

        // Mastery is exposed as `p` inside formulas.
        public float Mastery;

        // Turn speed (seconds) and movement speed.
        public int Speed;
        public int MoveSpeed;

        // Bonus threat / shred multipliers (0.15f => +15%).
        public float Threat;
        public float Shred;

        // Combat utility stats to ease playtest iteration.
        public int HealthRegenPerTurn;
        public int ArmorRegenPerTurn;
        public int Shield;

        public void Clamp()
        {
            if (HP > MaxHP) HP = MaxHP;
            if (HP < 0) HP = 0;

            ClampResource(ref Energy, ref MaxEnergy);
            ClampResource(ref Discipline, ref MaxDiscipline);
            ClampResource(ref Iron, ref MaxIron);
            ClampResource(ref Rage, ref MaxRage);
            ClampResource(ref Versatility, ref MaxVersatility);
            ClampResource(ref Gunpowder, ref MaxGunpowder);
            ClampResource(ref Point, ref MaxPoint);
            ClampResource(ref Combo, ref MaxCombo);
            ClampResource(ref Punch, ref MaxPunch);
            ClampResource(ref Qi, ref MaxQi);
            ClampResource(ref Vision, ref MaxVision);
            ClampResource(ref Posture, ref MaxPosture);

            NormalizeDecimalStats();
        }

        /// <summary>
        /// Ensures decimal based ratings keep the agreed three-decimal precision.
        /// </summary>
        public void NormalizeDecimalStats()
        {
            Crit = RoundToThreeDecimals(Crit);
            Mastery = RoundToThreeDecimals(Mastery);
            DamageIncrease = RoundToThreeDecimals(DamageIncrease);
            DamageIncreaseSecondary = RoundToThreeDecimals(DamageIncreaseSecondary);
            HealIncrease = RoundToThreeDecimals(HealIncrease);
            DamageReduction = RoundToThreeDecimals(DamageReduction);
            DamageTakenMultiplier = RoundToThreeDecimals(DamageTakenMultiplier);
            Threat = RoundToThreeDecimals(Threat);
            Shred = RoundToThreeDecimals(Shred);
        }

        /// <summary>
        /// Returns the crit chance formatted as a percentage (three decimals precision).
        /// </summary>
        public float GetCritPercent() => RoundToThreeDecimals(Crit * 100f);

        /// <summary>
        /// Returns the mastery value formatted as a percentage (three decimals precision).
        /// </summary>
        public float GetMasteryPercent(float conversionRatio = 1f) => RoundToThreeDecimals(Mastery * conversionRatio * 100f);

        /// <summary>
        /// Returns the threat multiplier ready for UI display (percentage form).
        /// </summary>
        public float GetThreatPercent() => RoundToThreeDecimals(NormalizePercent(Threat) * 100f);

        /// <summary>
        /// Returns the shred multiplier ready for UI display (percentage form).
        /// </summary>
        public float GetShredPercent() => RoundToThreeDecimals(NormalizePercent(Shred) * 100f);

        private static float RoundToThreeDecimals(float value)
        {
            return (float)Math.Round(value, 3, MidpointRounding.AwayFromZero);
        }

        private static float NormalizePercent(float value)
        {
            if (value > 1f)
                return value / 100f;
            if (value < 0f)
                return 0f;
            return value;
        }
        private static void ClampResource(ref int current, ref int max, int minimumMax = 0)
        {
            if (max < minimumMax)
                max = minimumMax;
            if (current > max)
                current = max;
            if (current < 0)
                current = 0;
        }

        public Stats Clone()
        {
            return (Stats)MemberwiseClone();
        }
    }
}

