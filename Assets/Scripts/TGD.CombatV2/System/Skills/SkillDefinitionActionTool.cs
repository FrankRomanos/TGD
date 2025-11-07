using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TGD.CombatV2.Targeting;
using TGD.CoreV2;
using TGD.DataV2;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    [AddComponentMenu("TGD/CombatV2/Skill Definition Action Tool")]
    public sealed class SkillDefinitionActionTool : ChainActionBase, IActionResolveEffect, IFullRoundActionTool, IActionCostPreviewV2
    {
        [SerializeField]
        [Tooltip("Skill definition driving this action's configuration.")]
        private SkillDefinitionV2 definition;

        [SerializeField]
        [Tooltip("Fallback targeting rule used when no definition is assigned.")]
        private SkillDefinitionTargetRule fallbackTargetRule = SkillDefinitionTargetRule.AnyClick;

        int _preparedSeconds;

        public SkillDefinitionV2 Definition => definition;

        public override ActionKind Kind
        {
            get
            {
                return ConvertKind(definition != null ? definition.ActionKind : SkillDefinitionActionKind.Standard);
            }
        }

        void Reset()
        {
            ApplyDefinition(false);
        }

        protected override void Awake()
        {
            base.Awake();
            ApplyDefinition(true);
        }

        void OnValidate()
        {
            ApplyDefinition(false);
        }

        public void SetDefinition(SkillDefinitionV2 value)
        {
            definition = value;
            ApplyDefinition(true);
        }

        public override Sprite Icon
        {
            get
            {
                if (definition != null && definition.Icon != null)
                    return definition.Icon;
                return base.Icon;
            }
        }

        void ApplyDefinition(bool registerCooldown)
        {
            if (definition == null)
            {
                skillId = string.Empty;
                icon = null;
                timeCostSeconds = 0;
                energyCost = 0;
                targetMode = ConvertTargetRule(fallbackTargetRule);
                maxRangeHexes = -1;
                cooldownSeconds = 0;
                return;
            }

            skillId = definition.Id;
            icon = definition.Icon;
            targetMode = ConvertTargetRule(definition.TargetRule);
            maxRangeHexes = definition.MaxRangeHexes;

            var cost = EvaluateCosts();
            timeCostSeconds = cost.seconds;
            energyCost = cost.energy;
            cooldownSeconds = definition.CooldownSeconds;

            if (registerCooldown)
            {
                var catalog = ActionCooldownCatalog.Instance;
                if (catalog != null)
                    catalog.RegisterSeconds(skillId, definition.CooldownSeconds);
            }
        }

        static ActionKind ConvertKind(SkillDefinitionActionKind kind)
        {
            return kind switch
            {
                SkillDefinitionActionKind.Reaction => ActionKind.Reaction,
                SkillDefinitionActionKind.Derived => ActionKind.Derived,
                SkillDefinitionActionKind.FullRound => ActionKind.FullRound,
                SkillDefinitionActionKind.Sustained => ActionKind.Sustained,
                SkillDefinitionActionKind.Free => ActionKind.Free,
                _ => ActionKind.Standard,
            };
        }

        static TargetMode ConvertTargetRule(SkillDefinitionTargetRule rule)
        {
            return rule switch
            {
                SkillDefinitionTargetRule.GroundOnly => TargetMode.GroundOnly,
                SkillDefinitionTargetRule.EnemyOnly => TargetMode.EnemyOnly,
                SkillDefinitionTargetRule.AllyOnly => TargetMode.AllyOnly,
                SkillDefinitionTargetRule.SelfOnly => TargetMode.SelfOnly,
                SkillDefinitionTargetRule.EnemyOrGround => TargetMode.EnemyOrGround,
                SkillDefinitionTargetRule.AllyOrGround => TargetMode.AllyOrGround,
                SkillDefinitionTargetRule.AnyUnit => TargetMode.AnyUnit,
                _ => TargetMode.AnyClick,
            };
        }

        (int seconds, int energy) EvaluateCosts()
        {
            if (definition == null || !definition.IsActive)
                return (0, 0);

            switch (definition.CostAuthority)
            {
                case SkillCostAuthority.AttackProfile:
                {
                    int secs = ctx != null ? ctx.AttackSeconds : AttackProfileRules.DefaultSeconds;
                    int energy = ctx != null ? ctx.AttackEnergyCost : AttackProfileRules.DefaultEnergyCost;
                    return (Mathf.Max(0, secs), Mathf.Max(0, energy));
                }
                case SkillCostAuthority.MoveProfile:
                {
                    int secs = ctx != null ? ctx.MoveBaseSecondsCeil : Mathf.Max(1, Mathf.CeilToInt(MoveProfileRules.DefaultSeconds));
                    int perSecond = ctx != null ? ctx.MoveEnergyPerSecond : MoveProfileRules.DefaultEnergyPerSecond;
                    int energy = Mathf.Max(0, perSecond * Mathf.Max(0, secs));
                    return (Mathf.Max(0, secs), energy);
                }
                default:
                    return (definition.TimeCostSeconds, definition.EnergyCost);
            }
        }

        public override IEnumerator OnConfirm(Hex hex)
        {
            ApplyDefinition(true);

            var cost = EvaluateCosts();
            timeCostSeconds = cost.seconds;
            energyCost = cost.energy;

            yield return base.OnConfirm(hex);

            if (Kind != ActionKind.FullRound)
                LogConfirm(ResolveUnit(), hex);
        }

        bool IActionCostPreviewV2.TryPeekCost(out int seconds, out int energy)
        {
            ApplyDefinition(false);
            var cost = EvaluateCosts();
            seconds = cost.seconds;
            energy = cost.energy;
            return definition != null && definition.IsActive;
        }

        public void OnResolve(Unit unit, Hex target)
        {
            LogResolve(unit, target);
        }

        public int FullRoundRounds
            => definition != null ? definition.FullRoundRounds : 1;

        public void PrepareFullRoundSeconds(int seconds)
        {
            _preparedSeconds = Mathf.Max(0, seconds);
        }

        public void TriggerFullRoundImmediate(Unit unit, TurnManagerV2 turnManager, FullRoundQueuedPlan plan)
        {
            _ = turnManager;
            _preparedSeconds = Mathf.Max(0, plan.plannedSeconds);
            LogConfirm(unit, plan.valid ? plan.target : (Hex?)null);
        }

        public void TriggerFullRoundResolution(Unit unit, TurnManagerV2 turnManager, FullRoundQueuedPlan plan)
        {
            _ = turnManager;
            LogResolve(unit, plan.valid ? plan.target : (Hex?)null);
        }

        void LogConfirm(Unit unit, Hex? target)
        {
            if (definition == null)
                return;
            WriteLogs(definition.ConfirmLogs, unit, target, Kind == ActionKind.FullRound ? "FullRound-W2" : "Confirm");
        }

        void LogResolve(Unit unit, Hex? target)
        {
            if (definition == null)
                return;
            WriteLogs(definition.ResolveLogs, unit, target, Kind == ActionKind.FullRound ? "FullRound-W4" : "Resolve");
        }

        void WriteLogs(IReadOnlyList<string> logs, Unit unit, Hex? target, string stage)
        {
            if (logs == null || logs.Count == 0)
                return;

            string unitLabel = unit != null ? TurnManagerV2.FormatUnitLabel(unit) : "?";
            string targetLabel = target.HasValue ? target.Value.ToString() : "None";
            string skillLabel = definition != null ? definition.DisplayName : skillId;

            for (int i = 0; i < logs.Count; i++)
            {
                var line = logs[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                Debug.Log($"[Skill] {skillLabel} ({skillId}) stage={stage} unit={unitLabel} target={targetLabel} msg={line}", this);
            }
        }
    }
}
