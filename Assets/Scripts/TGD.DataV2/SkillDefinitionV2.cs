using System;
using TGD.CombatV2;
using TGD.CombatV2.Targeting;
using UnityEngine;

namespace TGD.DataV2
{
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
        private ActionKind actionKind = ActionKind.Standard;

        [SerializeField]
        [Tooltip("Legal target rule resolved by DefaultTargetValidator.")]
        private TargetMode targetRule = TargetMode.AnyClick;

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
        public ActionKind ActionKind => actionKind;
        public TargetMode TargetRule => targetRule;
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
            ActionKind actionKind = ActionKind.Standard,
            TargetMode targetRule = TargetMode.AnyClick,
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
