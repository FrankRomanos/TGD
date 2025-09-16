using UnityEngine;

namespace TGD.Data
{
    /// <summary>
    /// Serialized settings that describe the behaviour of the "posture" mastery engine.
    /// </summary>
    [System.Serializable]
    public class MasteryPostureSettings
    {
        [Tooltip("If true the unit's armor value is always locked at 0.")]
        public bool lockArmorToZero = true;

        [Tooltip("Share of prevented armor gain that is converted into immediate HP.")]
        [Range(0f, 1f)]
        public float armorToHpRatio = 0.5f;

        [Tooltip("Share of prevented armor gain that increases maximum energy (or stamina).")]
        [Range(0f, 1f)]
        public float armorToEnergyRatio = 0.5f;

        [Tooltip("Posture resource to operate on (allows other effects to interact with the same pool).")]
        public ResourceType postureResource = ResourceType.posture;

        [Tooltip("How much posture capacity the unit has relative to current max HP (1 = 100%).")]
        [Range(0f, 5f)]
        public float postureMaxHealthRatio = 1f;

        [Tooltip("Optional expression to override the computed max posture (leave empty to use the ratio).")]
        public string postureMaxExpression = string.Empty;

        [Tooltip("Expression describing how mastery feeds into the damage-to-posture conversion (e.g. 'mastery * 0.8').")]
        public string masteryScalingExpression = "mastery";

        [Tooltip("Fraction of missing posture restored automatically every turn.")]
        [Range(0f, 1f)]
        public float postureRecoveryPercentPerTurn = 0.1f;

        [Tooltip("Additional damage multiplier received while the stance is broken.")]
        public float postureBreakExtraDamageMultiplier = 1.5f;

        [Tooltip("Status skill applied when the posture reaches zero (e.g. a 'cower' state).")]
        public string postureBreakStatusSkillID;

        [Tooltip("Number of rounds the break status persists.")]
        [Range(0, 10)]
        public int postureBreakDurationTurns = 1;

        [Tooltip("If true the unit skips its next turn when posture breaks.")]
        public bool postureBreakSkipsTurn = true;
    }
}