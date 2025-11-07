using System;
using UnityEngine;

namespace TGD.DataV2
{
    /// <summary>
    /// Skill taxonomy used for authoring. Mirrors CombatV2.ActionKind without introducing a dependency.
    /// </summary>
    public enum SkillDefinitionActionKind
    {
        Standard = 0,
        Reaction = 1,
        Derived = 2,
        FullRound = 3,
        Sustained = 4,
        Free = 5
    }

    /// <summary>
    /// Targeting options aligned with CombatV2.Targeting.TargetMode without a compile-time dependency.
    /// </summary>
    public enum SkillDefinitionTargetRule
    {
        AnyClick = 0,
        GroundOnly = 1,
        EnemyOnly = 2,
        AllyOnly = 3,
        SelfOnly = 4,
        EnemyOrGround = 5,
        AllyOrGround = 6,
        AnyUnit = 7
    }

    public enum SkillKind
    {
        Active,
        Passive
    }

    [CreateAssetMenu(menuName = "TGD/Skills/SkillDefinitionV2", fileName = "SkillDefinitionV2")]
    public sealed class SkillDefinitionV2 : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Unique identifier consumed by CAM / TMV2 / cooldown systems.")]
        private string id = string.Empty;

        [SerializeField]
        [Tooltip("Skill category. Passive skills ignore targeting & costs.")]
        private SkillKind kind = SkillKind.Active;

        [SerializeField]
        [Tooltip("Display label exposed to UI layers. Falls back to Id if empty.")]
        private string displayName = string.Empty;

        [SerializeField]
        [Tooltip("Optional icon exposed to UI layers.")]
        private Sprite icon;

        [Header("Active Skill Settings")]
        [SerializeField]
        [Tooltip("Combat action category consumed by CAM + ActionRuleBook.")]
        private SkillDefinitionActionKind actionKind = SkillDefinitionActionKind.Standard;

        [SerializeField]
        [Tooltip("Legal target rule resolved by DefaultTargetValidator.")]
        private SkillDefinitionTargetRule targetRule = SkillDefinitionTargetRule.AnyClick;

        [SerializeField, Min(0)]
        [Tooltip("Time cost in seconds resolved at confirm (W2).")]
        private int timeCostSeconds = 0;

        [SerializeField, Min(0)]
        [Tooltip("Energy cost resolved at confirm (W2).")]
        private int energyCost = 0;

        public string Id => Normalize(id);
        public SkillKind Kind => kind;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? Id : displayName.Trim();
        public Sprite Icon => icon;
        public SkillDefinitionActionKind ActionKind => actionKind;
        public SkillDefinitionTargetRule TargetRule => targetRule;
        public int TimeCostSeconds => Mathf.Max(0, timeCostSeconds);
        public int EnergyCost => Mathf.Max(0, energyCost);

        public bool IsActive => kind == SkillKind.Active;
        public bool IsPassive => kind == SkillKind.Passive;

        public bool TryGetCosts(out int seconds, out int energy)
        {
            if (!IsActive)
            {
                seconds = 0;
                energy = 0;
                return false;
            }

            seconds = TimeCostSeconds;
            energy = EnergyCost;
            return true;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return value.Trim();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            id = Normalize(id);
            displayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();
            if (kind == SkillKind.Passive)
            {
                timeCostSeconds = 0;
                energyCost = 0;
            }
        }

        public void EditorInitialize(
            string id,
            SkillKind kind = SkillKind.Active,
            SkillDefinitionActionKind actionKind = SkillDefinitionActionKind.Standard,
            SkillDefinitionTargetRule targetRule = SkillDefinitionTargetRule.AnyClick,
            int timeCostSeconds = 0,
            int energyCost = 0,
            string displayName = null,
            Sprite icon = null)
        {
            this.id = Normalize(id);
            this.kind = kind;
            this.actionKind = actionKind;
            this.targetRule = targetRule;
            this.timeCostSeconds = Mathf.Max(0, timeCostSeconds);
            this.energyCost = Mathf.Max(0, energyCost);
            this.displayName = string.IsNullOrWhiteSpace(displayName) ? this.id : displayName.Trim();
            this.icon = icon;
        }
#endif
    }
}
