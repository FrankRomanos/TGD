using System;
using System.Collections.Generic;
using UnityEngine;
using TGD.Data;

namespace TGD.Combat
{
    public interface ISkillResolver
    {
        SkillDefinition FindSkill(string skillID);
    }

    public class DictionarySkillResolver : ISkillResolver
    {
        private readonly Dictionary<string, SkillDefinition> skills;

        public DictionarySkillResolver(IEnumerable<SkillDefinition> source)
        {
            skills = new Dictionary<string, SkillDefinition>(StringComparer.OrdinalIgnoreCase);
            if (source == null)
                return;

            foreach (var skill in source)
            {
                if (skill == null || string.IsNullOrWhiteSpace(skill.skillID))
                    continue;
                skills[skill.skillID] = skill;
            }
        }

        public SkillDefinition FindSkill(string skillID)
        {
            if (string.IsNullOrWhiteSpace(skillID))
                return null;
            skills.TryGetValue(skillID, out var skill);
            return skill;
        }
    }

    public class EffectContext
    {
        public EffectContext(Unit caster, SkillDefinition skill)
        {
            Caster = caster;
            Skill = skill;
            Allies = new List<Unit>();
            Enemies = new List<Unit>();
            ResourceValues = new Dictionary<ResourceType, float>();
            ResourceSpent = new Dictionary<ResourceType, float>();
            CustomVariables = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            ProfessionScaling = 1f;

            if (caster != null)
            {
                Allies.Add(caster);
                ResourceValues[ResourceType.HP] = caster.Stats.HP;
                ResourceValues[ResourceType.Energy] = caster.Stats.Energy;
                ResourceValues[ResourceType.posture] = caster.Stats.Posture;
            }
        }

        public Unit Caster { get; }
        public SkillDefinition Skill { get; private set; }
        public Unit PrimaryTarget { get; set; }
        public Unit SecondaryTarget { get; set; }
        public List<Unit> Allies { get; }
        public List<Unit> Enemies { get; }
        public Dictionary<ResourceType, float> ResourceValues { get; }
        public Dictionary<ResourceType, float> ResourceSpent { get; }
        public Dictionary<string, float> CustomVariables { get; }
        public float ProfessionScaling { get; set; }
        public float IncomingDamage { get; set; }
        public bool ConditionAfterAttack { get; set; }
        public bool ConditionOnCrit { get; set; }
        public bool ConditionOnCooldownEnd { get; set; }
        public bool ConditionAfterSkillUse { get; set; }
        public bool ConditionSkillStateActive { get; set; }
        public bool ConditionOnResourceSpend { get; set; }
        public ResourceType? LastResourceSpendType { get; set; }
        public float LastResourceSpendAmount { get; set; }
        public ISkillResolver SkillResolver { get; set; }

        private int skillLevelOverride;

        public bool HasSkillLevelOverride => skillLevelOverride > 0;

        public void OverrideSkillLevel(int level)
        {
            skillLevelOverride = Mathf.Clamp(level, 1, 4);
        }

        public int ResolveSkillLevel(SkillDefinition skill)
        {
            if (HasSkillLevelOverride)
                return skillLevelOverride;
            if (skill != null)
                return Mathf.Clamp(skill.skillLevel, 1, 4);
            return skillLevelOverride > 0 ? skillLevelOverride : 1;
        }

        public float GetResourceAmount(ResourceType type)
        {
            return ResourceValues.TryGetValue(type, out var value) ? value : 0f;
        }

        public float GetResourceSpent(ResourceType type)
        {
            return ResourceSpent.TryGetValue(type, out var value) ? value : 0f;
        }

        public EffectContext CloneForSkill(SkillDefinition skill, bool inheritSkillLevelOverride = false)
        {
            var clone = new EffectContext(Caster, skill)
            {
                PrimaryTarget = PrimaryTarget,
                SecondaryTarget = SecondaryTarget,
                ProfessionScaling = ProfessionScaling,
                IncomingDamage = IncomingDamage,
                ConditionAfterAttack = ConditionAfterAttack,
                ConditionOnCrit = ConditionOnCrit,
                ConditionOnCooldownEnd = ConditionOnCooldownEnd,
                ConditionAfterSkillUse = ConditionAfterSkillUse,
                ConditionSkillStateActive = ConditionSkillStateActive,
                ConditionOnResourceSpend = ConditionOnResourceSpend,
                LastResourceSpendType = LastResourceSpendType,
                LastResourceSpendAmount = LastResourceSpendAmount,
                SkillResolver = SkillResolver
            };

            clone.Allies.Clear();
            clone.Allies.AddRange(Allies);
            clone.Enemies.Clear();
            clone.Enemies.AddRange(Enemies);

            clone.ResourceValues.Clear();
            foreach (var kvp in ResourceValues)
                clone.ResourceValues[kvp.Key] = kvp.Value;

            clone.ResourceSpent.Clear();
            foreach (var kvp in ResourceSpent)
                clone.ResourceSpent[kvp.Key] = kvp.Value;

            clone.CustomVariables.Clear();
            foreach (var kvp in CustomVariables)
                clone.CustomVariables[kvp.Key] = kvp.Value;

            if (inheritSkillLevelOverride && HasSkillLevelOverride)
                clone.skillLevelOverride = skillLevelOverride;

            return clone;
        }
    }

    public class EffectInterpretationResult
    {
        public List<DamagePreview> Damage { get; } = new();
        public List<HealPreview> Healing { get; } = new();
        public List<ResourceChangePreview> ResourceChanges { get; } = new();
        public List<StatusApplicationPreview> StatusApplications { get; } = new();
        public List<ConditionalPreview> Conditionals { get; } = new();
        public List<SkillModificationPreview> SkillModifications { get; } = new();
        public List<SkillReplacementPreview> SkillReplacements { get; } = new();
        public List<ActionModificationPreview> ActionModifications { get; } = new();
        public List<CooldownModificationPreview> CooldownModifications { get; } = new();
        public List<AttributeModifierPreview> AttributeModifiers { get; } = new();
        public List<ScalingBuffPreview> ScalingBuffs { get; } = new();
        public List<MovePreview> Moves { get; } = new();
        public List<MasteryPosturePreview> MasteryPosture { get; } = new();
        public List<string> Logs { get; } = new();

        public void Append(EffectInterpretationResult other)
        {
            if (other == null)
                return;
            Damage.AddRange(other.Damage);
            Healing.AddRange(other.Healing);
            ResourceChanges.AddRange(other.ResourceChanges);
            StatusApplications.AddRange(other.StatusApplications);
            Conditionals.AddRange(other.Conditionals);
            SkillModifications.AddRange(other.SkillModifications);
            SkillReplacements.AddRange(other.SkillReplacements);
            ActionModifications.AddRange(other.ActionModifications);
            CooldownModifications.AddRange(other.CooldownModifications);
            AttributeModifiers.AddRange(other.AttributeModifiers);
            ScalingBuffs.AddRange(other.ScalingBuffs);
            Moves.AddRange(other.Moves);
            MasteryPosture.AddRange(other.MasteryPosture);
            Logs.AddRange(other.Logs);
        }

        public void AddLog(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                Logs.Add(message);
        }
    }

    public class DamagePreview
    {
        public Unit Source { get; set; }
        public Unit Target { get; set; }
        public float Amount { get; set; }
        public DamageSchool School { get; set; }
        public bool CanCrit { get; set; }
        public float Probability { get; set; }
        public string Expression { get; set; }
        public EffectCondition Condition { get; set; }
    }

    public class HealPreview
    {
        public Unit Source { get; set; }
        public Unit Target { get; set; }
        public float Amount { get; set; }
        public bool CanCrit { get; set; }
        public float Probability { get; set; }
        public string Expression { get; set; }
        public EffectCondition Condition { get; set; }
    }

    public class ResourceChangePreview
    {
        public ResourceType Resource { get; set; }
        public Unit Target { get; set; }
        public float Amount { get; set; }
        public float Probability { get; set; }
        public string Expression { get; set; }
        public EffectCondition Condition { get; set; }
    }

    public class StatusApplicationPreview
    {
        public string StatusSkillID { get; set; }
        public int Duration { get; set; }
        public bool IsInstant { get; set; }
        public int StackCount { get; set; }
        public TargetType Target { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
        public EffectInterpretationResult InstantResult { get; set; }
    }

    public class ConditionalPreview
    {
        public ResourceType Resource { get; set; }
        public CompareOp Comparison { get; set; }
        public float CompareValue { get; set; }
        public float CurrentValue { get; set; }
        public bool Succeeded { get; set; }
    }

    public class SkillModificationPreview
    {
        public string TargetSkillID { get; set; }
        public SkillModifyType ModifyType { get; set; }
        public SkillModifyOperation Operation { get; set; }
        public ModifierType ModifierType { get; set; }
        public string ValueExpression { get; set; }
        public bool AffectAllCosts { get; set; }
        public CostResourceType CostResource { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
    }

    public class SkillReplacementPreview
    {
        public string TargetSkillID { get; set; }
        public string NewSkillID { get; set; }
        public bool InheritCooldown { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
    }

    public class ActionModificationPreview
    {
        public string TargetSkillID { get; set; }
        public ActionType ActionFilter { get; set; }
        public ActionModifyType ModifyType { get; set; }
        public ModifierType ModifierType { get; set; }
        public string ValueExpression { get; set; }
        public ActionType ActionTypeOverride { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
    }

    public class CooldownModificationPreview
    {
        public CooldownTargetScope Scope { get; set; }
        public string SelfSkillID { get; set; }
        public int Seconds { get; set; }
        public int Rounds { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
    }

    public class AttributeModifierPreview
    {
        public AttributeType Attribute { get; set; }
        public ModifierType ModifierType { get; set; }
        public string ValueExpression { get; set; }
        public int Duration { get; set; }
        public int StackCount { get; set; }
        public TargetType Target { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
    }

    public class ScalingBuffPreview
    {
        public ResourceType Resource { get; set; }
        public string ValuePerResource { get; set; }
        public int MaxStacks { get; set; }
        public ScalingAttribute Attribute { get; set; }
        public TargetType Target { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
    }

    public class MovePreview
    {
        public MoveSubject Subject { get; set; }
        public MoveExecution Execution { get; set; }
        public MoveDirection Direction { get; set; }
        public int Distance { get; set; }
        public int MaxDistance { get; set; }
        public Vector2Int Offset { get; set; }
        public bool ForceMovement { get; set; }
        public bool AllowPartialMove { get; set; }
        public bool IgnoreObstacles { get; set; }
        public bool StopAdjacentToTarget { get; set; }
        public TargetType Target { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
    }

    public class MasteryPosturePreview
    {
        public bool LockArmorToZero { get; set; }
        public float ArmorToHpRatio { get; set; }
        public float ArmorToEnergyRatio { get; set; }
        public ResourceType PostureResource { get; set; }
        public float PostureMaxHealthRatio { get; set; }
        public string PostureMaxExpression { get; set; }
        public string MasteryScalingExpression { get; set; }
        public string DamageConversionExpression { get; set; }
        public float RecoveryPercentPerTurn { get; set; }
        public float BreakDamageMultiplier { get; set; }
        public string BreakStatusSkillID { get; set; }
        public int BreakDuration { get; set; }
        public bool BreakSkipsTurn { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
    }
}