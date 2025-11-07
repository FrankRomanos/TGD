using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Where to resolve the authoritative cost data for a skill.
    /// </summary>
    public enum SkillCostAuthority
    {
        /// <summary>Use the serialized values on the definition.</summary>
        Definition = 0,

        /// <summary>Use the owning unit's <see cref="TGD.CoreV2.AttackProfileV2"/>.</summary>
        AttackProfile = 1,

        /// <summary>Use the owning unit's <see cref="TGD.CoreV2.MoveProfileV2"/>.</summary>
        MoveProfile = 2
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

        [SerializeField]
        [Tooltip("Source of truth for time/energy costs. Attack & Move defer to their profiles.")]
        private SkillCostAuthority costAuthority = SkillCostAuthority.Definition;

        [SerializeField, Min(0)]
        [Tooltip("Time cost in seconds resolved at confirm (W2).")]
        private int timeCostSeconds = 0;

        [SerializeField, Min(0)]
        [Tooltip("Energy cost resolved at confirm (W2).")]
        private int energyCost = 0;

        [SerializeField]
        [Tooltip("Maximum targeting distance in hexes. -1 for unlimited.")]
        private int maxRangeHexes = -1;

        [SerializeField, Min(0)]
        [Tooltip("Cooldown applied (seconds) once the action confirms in W2.")]
        private int cooldownSeconds = 0;

        [SerializeField]
        [Tooltip("For derived actions: the base skill identifier this action follows.")]
        private string derivedFromSkillId = string.Empty;

        [SerializeField, Min(1)]
        [Tooltip("For full-round actions: number of rounds before the resolution triggers.")]
        private int fullRoundRounds = 1;

        [SerializeField]
        [Tooltip("Optional log lines printed during W2/confirm stage.")]
        private string[] confirmLogs = Array.Empty<string>();

        [SerializeField]
        [Tooltip("Optional log lines printed during W4/resolve stage.")]
        private string[] resolveLogs = Array.Empty<string>();

        public string Id => Normalize(id);
        public SkillKind Kind => kind;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? Id : displayName.Trim();
        public Sprite Icon => icon;
        public SkillDefinitionActionKind ActionKind => actionKind;
        public SkillDefinitionTargetRule TargetRule => targetRule;
        public SkillCostAuthority CostAuthority => costAuthority;
        public int TimeCostSeconds => Mathf.Max(0, timeCostSeconds);
        public int EnergyCost => Mathf.Max(0, energyCost);
        public int MaxRangeHexes => Mathf.Max(-1, maxRangeHexes);
        public int CooldownSeconds => Mathf.Max(0, cooldownSeconds);
        public string DerivedFromSkillId => actionKind == SkillDefinitionActionKind.Derived ? Normalize(derivedFromSkillId) : string.Empty;
        public int FullRoundRounds => Mathf.Max(1, fullRoundRounds);
        public IReadOnlyList<string> ConfirmLogs => confirmLogs ?? Array.Empty<string>();
        public IReadOnlyList<string> ResolveLogs => resolveLogs ?? Array.Empty<string>();

        public bool IsActive => kind == SkillKind.Active;
        public bool IsPassive => kind == SkillKind.Passive;

        public bool TryGetCosts(out int seconds, out int energy)
        {
            if (!IsActive || costAuthority != SkillCostAuthority.Definition)
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
            maxRangeHexes = Mathf.Max(-1, maxRangeHexes);
            cooldownSeconds = Mathf.Max(0, cooldownSeconds);
            fullRoundRounds = Mathf.Max(1, fullRoundRounds);
            if (kind == SkillKind.Passive || costAuthority != SkillCostAuthority.Definition)
            {
                timeCostSeconds = 0;
                energyCost = 0;
            }
            derivedFromSkillId = actionKind == SkillDefinitionActionKind.Derived ? Normalize(derivedFromSkillId) : string.Empty;
            if (confirmLogs == null)
                confirmLogs = Array.Empty<string>();
            if (resolveLogs == null)
                resolveLogs = Array.Empty<string>();
        }

        public void EditorInitialize(
            string id,
            SkillKind kind = SkillKind.Active,
            SkillDefinitionActionKind actionKind = SkillDefinitionActionKind.Standard,
            SkillDefinitionTargetRule targetRule = SkillDefinitionTargetRule.AnyClick,
            SkillCostAuthority costAuthority = SkillCostAuthority.Definition,
            int timeCostSeconds = 0,
            int energyCost = 0,
            string displayName = null,
            Sprite icon = null,
            int maxRangeHexes = -1,
            int cooldownSeconds = 0,
            string derivedFromSkillId = null,
            int fullRoundRounds = 1,
            IEnumerable<string> confirmLogs = null,
            IEnumerable<string> resolveLogs = null)
        {
            this.id = Normalize(id);
            this.kind = kind;
            this.actionKind = actionKind;
            this.targetRule = targetRule;
            this.costAuthority = costAuthority;
            if (this.kind == SkillKind.Passive || this.costAuthority != SkillCostAuthority.Definition)
            {
                this.timeCostSeconds = 0;
                this.energyCost = 0;
            }
            else
            {
                this.timeCostSeconds = Mathf.Max(0, timeCostSeconds);
                this.energyCost = Mathf.Max(0, energyCost);
            }
            this.displayName = string.IsNullOrWhiteSpace(displayName) ? this.id : displayName.Trim();
            this.icon = icon;
            this.maxRangeHexes = Mathf.Max(-1, maxRangeHexes);
            this.cooldownSeconds = Mathf.Max(0, cooldownSeconds);
            this.derivedFromSkillId = actionKind == SkillDefinitionActionKind.Derived
                ? Normalize(derivedFromSkillId)
                : string.Empty;
            this.fullRoundRounds = Mathf.Max(1, fullRoundRounds);
            this.confirmLogs = confirmLogs != null ? new List<string>(confirmLogs).ToArray() : Array.Empty<string>();
            this.resolveLogs = resolveLogs != null ? new List<string>(resolveLogs).ToArray() : Array.Empty<string>();
        }
#endif
    }
}
