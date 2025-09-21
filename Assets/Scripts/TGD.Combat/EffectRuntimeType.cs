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

    public class DotHotStatusSnapshot
    {
        public string SkillID { get; set; }
        public TargetType Target { get; set; }
        public int Stacks { get; set; }
        public bool IsHot { get; set; }
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
            ResourceMaxValues = new Dictionary<ResourceType, float>();
            CustomVariables = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            ActiveSkillStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ActiveDotHotStatuses = new List<DotHotStatusSnapshot>();
            ActiveSkillStateStacks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            ConditionDistances = new Dictionary<ConditionTarget, float>();
            ConditionPathBlocked = new Dictionary<ConditionTarget, bool>();

            if (caster != null)
            {
                Allies.Add(caster);
                ResourceValues[ResourceType.HP] = caster.Stats.HP;
                ResourceValues[ResourceType.Energy] = caster.Stats.Energy;
                ResourceValues[ResourceType.posture] = caster.Stats.Posture;
                ResourceMaxValues[ResourceType.HP] = caster.Stats.MaxHP;
                ResourceMaxValues[ResourceType.Energy] = caster.Stats.MaxEnergy;
                ResourceMaxValues[ResourceType.posture] = caster.Stats.MaxPosture;
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
        public Dictionary<ResourceType, float> ResourceMaxValues { get; }
        public Dictionary<string, float> CustomVariables { get; }
        public float IncomingDamage { get; set; }
        public float IncomingHealing { get; set; }
        public float OutgoingDamage { get; set; }
        public float OutgoingHealing { get; set; }
        public HashSet<string> ActiveSkillStates { get; }
        public Dictionary<string, int> ActiveSkillStateStacks { get; }
        public List<DotHotStatusSnapshot> ActiveDotHotStatuses { get; }
        public bool ConditionAfterAttack { get; set; }
        public float IncomingDamageMitigated { get; set; }
        public bool ConditionOnPerformAttack { get; set; }
        public bool ConditionOnPerformHeal { get; set; }
        public bool ConditionOnCrit { get; set; }
        public bool ConditionOnCooldownEnd { get; set; }
        public bool ConditionAfterSkillUse { get; set; }
        public string LastSkillUsedID { get; set; }
        public bool ConditionSkillStateActive { get; set; }
        public bool ConditionOnResourceSpend { get; set; }
        public bool ConditionOnEffectEnd { get; set; }
        public bool ConditionOnDamageTaken { get; set; }
        public bool ConditionOnTurnBeginSelf { get; set; }
        public bool ConditionOnTurnBeginEnemy { get; set; }
        public bool ConditionOnTurnEndSelf { get; set; }
        public bool ConditionOnTurnEndEnemy { get; set; }
        public ResourceType? LastResourceSpendType { get; set; }
        public float LastResourceSpendAmount { get; set; }
        public ISkillResolver SkillResolver { get; set; }
        public float ConditionDotStacks { get; set; }
        public Dictionary<ConditionTarget, float> ConditionDistances { get; }
        public Dictionary<ConditionTarget, bool> ConditionPathBlocked { get; }
        public Unit ConditionEventTarget { get; set; }
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
        public float GetResourceMax(ResourceType type)
        {
            return ResourceMaxValues.TryGetValue(type, out var value) ? value : 0f;
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
                IncomingDamage = IncomingDamage,
                IncomingHealing = IncomingHealing,
                IncomingDamageMitigated = IncomingDamageMitigated,
                OutgoingDamage = OutgoingDamage,
                OutgoingHealing = OutgoingHealing,
                ConditionAfterAttack = ConditionAfterAttack,
                ConditionOnCrit = ConditionOnCrit,
                ConditionOnPerformAttack = ConditionOnPerformAttack,
                ConditionOnPerformHeal = ConditionOnPerformHeal,
                ConditionOnCooldownEnd = ConditionOnCooldownEnd,
                ConditionAfterSkillUse = ConditionAfterSkillUse,
                LastSkillUsedID = LastSkillUsedID,
                ConditionSkillStateActive = ConditionSkillStateActive,
                ConditionOnResourceSpend = ConditionOnResourceSpend,
                ConditionOnEffectEnd = ConditionOnEffectEnd,
                ConditionOnDamageTaken = ConditionOnDamageTaken,
                ConditionOnTurnBeginSelf = ConditionOnTurnBeginSelf,
                ConditionOnTurnBeginEnemy = ConditionOnTurnBeginEnemy,
                ConditionOnTurnEndSelf = ConditionOnTurnEndSelf,
                ConditionOnTurnEndEnemy = ConditionOnTurnEndEnemy,
                LastResourceSpendType = LastResourceSpendType,
                LastResourceSpendAmount = LastResourceSpendAmount,
                SkillResolver = SkillResolver,
                ConditionEventTarget = ConditionEventTarget
            };

            clone.Allies.Clear();
            clone.Allies.AddRange(Allies);
            clone.Enemies.Clear();
            clone.Enemies.AddRange(Enemies);

            clone.ResourceValues.Clear();
            foreach (var kvp in ResourceValues)
                clone.ResourceValues[kvp.Key] = kvp.Value;

            clone.ResourceMaxValues.Clear();
            foreach (var kvp in ResourceMaxValues)
                clone.ResourceMaxValues[kvp.Key] = kvp.Value;

            clone.ResourceSpent.Clear();
            foreach (var kvp in ResourceSpent)
                clone.ResourceSpent[kvp.Key] = kvp.Value;

            clone.CustomVariables.Clear();
            foreach (var kvp in CustomVariables)
                clone.CustomVariables[kvp.Key] = kvp.Value;

            clone.ActiveSkillStates.Clear();
            foreach (var state in ActiveSkillStates)
                clone.ActiveSkillStates.Add(state);

            clone.ActiveSkillStateStacks.Clear();
            foreach (var kvp in ActiveSkillStateStacks)
                clone.ActiveSkillStateStacks[kvp.Key] = kvp.Value;

            clone.ActiveDotHotStatuses.Clear();
            foreach (var status in ActiveDotHotStatuses)
            {
                if (status == null)
                    continue;
                clone.ActiveDotHotStatuses.Add(new DotHotStatusSnapshot
                {
                    SkillID = status.SkillID,
                    Target = status.Target,
                    Stacks = status.Stacks,
                    IsHot = status.IsHot
                });
            }

            clone.ConditionDistances.Clear();
            foreach (var kvp in ConditionDistances)
                clone.ConditionDistances[kvp.Key] = kvp.Value;

            clone.ConditionPathBlocked.Clear();
            foreach (var kvp in ConditionPathBlocked)
                clone.ConditionPathBlocked[kvp.Key] = kvp.Value;

            if (inheritSkillLevelOverride && HasSkillLevelOverride)
                clone.skillLevelOverride = skillLevelOverride;
            
            clone.ConditionDotStacks = ConditionDotStacks;
            return clone;
        }

        public int GetSkillStateStacks(string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
                return 0;

            if (ActiveSkillStateStacks.TryGetValue(skillId, out int stacks))
                return stacks;

            return ActiveSkillStates.Contains(skillId) ? 1 : 0;
        }

        public float GetDistance(ConditionTarget target)
        {
            if (ConditionDistances.TryGetValue(target, out float distance))
                return distance;

            return 0f;
        }

        public bool IsPathBlocked(ConditionTarget target)
        {
            return ConditionPathBlocked.TryGetValue(target, out bool blocked) && blocked;
        }
    }

    public class EffectInterpretationResult
    {
        public List<DamagePreview> Damage { get; } = new();
        public List<HealPreview> Healing { get; } = new();
        public List<ResourceChangePreview> ResourceChanges { get; } = new();
        public List<StatusApplicationPreview> StatusApplications { get; } = new();
        public List<StatusModificationPreview> StatusModifications { get; } = new();
        public List<ConditionalPreview> Conditionals { get; } = new();
        public List<SkillModificationPreview> SkillModifications { get; } = new();
        public List<SkillReplacementPreview> SkillReplacements { get; } = new();
        public List<ActionModificationPreview> ActionModifications { get; } = new();
        public List<DamageSchoolModificationPreview> DamageSchoolModifications { get; } = new();
        public List<CooldownModificationPreview> CooldownModifications { get; } = new();
        public List<AttributeModifierPreview> AttributeModifiers { get; } = new();
        public List<ScalingBuffPreview> ScalingBuffs { get; } = new();
        public List<MovePreview> Moves { get; } = new();
        public List<MasteryPosturePreview> MasteryPosture { get; } = new();
        public List<AuraPreview> Auras { get; } = new();
        public List<RandomOutcomePreview> RandomOutcomes { get; } = new();
        public List<RepeatEffectPreview> RepeatEffects { get; } = new();
        public List<ProbabilityModifierPreview> ProbabilityModifiers { get; } = new();
        public List<DefenceModificationPreview> DefenceModifications { get; } = new();
        public List<DotHotModifierPreview> DotHotModifiers { get; } = new();
        public List<SkillUseConditionPreview> SkillUseConditions { get; } = new();
        public List<string> Logs { get; } = new();

        public void Append(EffectInterpretationResult other)
        {
            if (other == null)
                return;
            Damage.AddRange(other.Damage);
            Healing.AddRange(other.Healing);
            ResourceChanges.AddRange(other.ResourceChanges);
            StatusApplications.AddRange(other.StatusApplications);
            StatusModifications.AddRange(other.StatusModifications);
            Conditionals.AddRange(other.Conditionals);
            SkillModifications.AddRange(other.SkillModifications);
            SkillReplacements.AddRange(other.SkillReplacements);
            ActionModifications.AddRange(other.ActionModifications);
            DamageSchoolModifications.AddRange(other.DamageSchoolModifications);
            CooldownModifications.AddRange(other.CooldownModifications);
            AttributeModifiers.AddRange(other.AttributeModifiers);
            ScalingBuffs.AddRange(other.ScalingBuffs);
            Moves.AddRange(other.Moves);
            MasteryPosture.AddRange(other.MasteryPosture);
            Auras.AddRange(other.Auras);
            RandomOutcomes.AddRange(other.RandomOutcomes);
            RepeatEffects.AddRange(other.RepeatEffects);
            ProbabilityModifiers.AddRange(other.ProbabilityModifiers);
            DotHotModifiers.AddRange(other.DotHotModifiers);
            DefenceModifications.AddRange(other.DefenceModifications);
            SkillUseConditions.AddRange(other.SkillUseConditions);
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
        public ImmunityScope ImmunityScope { get; set; }
        public float ExpectedNormalDamage { get; set; }
        public float ExpectedCriticalDamage { get; set; }
        public float ExpectedThreat { get; set; }
        public float ExpectedCriticalThreat { get; set; }
        public float ExpectedShred { get; set; }
        public float ExpectedCriticalShred { get; set; }
        public float CriticalMultiplier { get; set; }
        public float AttributeScalingMultiplier { get; set; }
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
        public bool FillToMax { get; set; }
        public ResourceModifyType ModifyType { get; set; }
        public bool AffectsMax { get; set; }
        public bool StateEnabled { get; set; }
    }

    public class StatusApplicationPreview
    {
        public string StatusSkillID { get; set; }
        public int Duration { get; set; }
        public bool IsInstant { get; set; }
        public bool IsPermanent { get; set; }
        public int StackCount { get; set; }
        public int MaxStacks { get; set; }
        public TargetType Target { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
        public EffectInterpretationResult InstantResult { get; set; }
    }
    public class StatusModificationPreview
    {
        public StatusModifyType ModifyType { get; set; }
        public TargetType Target { get; set; }
        public List<string> SkillIDs { get; set; } = new();
        public string ReplacementSkillID { get; set; }
        public bool ShowStacks { get; set; }
        public int StackCount { get; set; }
        public int MaxStacks { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
    }
    public class ConditionalPreview
    {
        public ResourceType Resource { get; set; }
        public CompareOp Comparison { get; set; }
        public float CompareValue { get; set; }
        public float CurrentValue { get; set; }
        public bool Succeeded { get; set; }
    }
    public class SkillUseConditionPreview
    {
        public SkillCostConditionType ConditionType { get; set; }
        public ConditionTarget Target { get; set; }
        public ResourceType Resource { get; set; }
        public CompareOp Comparison { get; set; }
        public float CompareValue { get; set; }
        public string CompareExpression { get; set; }
        public float CurrentValue { get; set; }
        public float MaxValue { get; set; }
        public float Distance { get; set; }
        public int MinDistance { get; set; }
        public int MaxDistance { get; set; }
        public bool RequireLineOfSight { get; set; }
        public bool PathBlocked { get; set; }
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
        public bool LimitEnabled { get; set; }
        public string LimitExpression { get; set; }
        public float LimitValue { get; set; }
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
        public string TargetTag { get; set; }
        public ActionModifyType ModifyType { get; set; }
        public ModifierType ModifierType { get; set; }
        public string ValueExpression { get; set; }
        public ActionType ActionTypeOverride { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
    }

    public class DamageSchoolModificationPreview
    {
        public string TargetSkillID { get; set; }
        public DamageSchoolModifyType ModifyType { get; set; }
        public DamageSchool TargetSchool { get; set; }
        public bool UseFilter { get; set; }
        public DamageSchool Filter { get; set; }
        public SkillModifyOperation Operation { get; set; }
        public ModifierType ModifierType { get; set; }
        public string ValueExpression { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
    }

    public class CooldownModificationPreview
    {
        public CooldownTargetScope Scope { get; set; }
        public string SelfSkillID { get; set; }
        public int Seconds { get; set; }
        public int Rounds { get; set; }
        public int turns {  get; set; }
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
        public ImmunityScope ImmunityScope { get; set; }
    }

    public class ScalingBuffPreview
    {
        public ResourceType Resource { get; set; }
        public string ValuePerResource { get; set; }
        public int MaxStacks { get; set; }
        public ScalingAttribute Attribute { get; set; }
        public SkillModifyOperation Operation { get; set; }
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

    public class AuraPreview
    {
        public TargetType Source { get; set; }
        public AuraRangeMode RangeMode { get; set; }
        public float Radius { get; set; }
        public float MinRadius { get; set; }
        public float MaxRadius { get; set; }
        public AuraEffectCategory Category { get; set; }
        public TargetType AffectedTargets { get; set; }
        public bool AffectsImmune { get; set; }
        public int Duration { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
        public EffectCondition OnEnterCondition { get; set; }
        public EffectCondition OnExitCondition { get; set; }
        public int HeartbeatSeconds { get; set; }
        public EffectInterpretationResult AdditionalEffects { get; set; }
    }
    public class RandomOutcomeOptionPreview
    {
        public string Label { get; set; }
        public string Description { get; set; }
        public float Probability { get; set; }
        public int Weight { get; set; }
        public ProbabilityModifierMode ProbabilityMode { get; set; }
        public EffectInterpretationResult Result { get; set; }
    }
    public class DamageSchoolBreakdownPreview
    {
        public DamageSchool School { get; set; }
        public string ValueExpression { get; set; }
        public float Value { get; set; }
    }

    public class DefenceModificationPreview
    {
        public DefenceModificationMode Mode { get; set; }
        public TargetType Target { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
        public int Duration { get; set; }
        public int StackCount { get; set; }
        public string ValueExpression { get; set; }
        public float Value { get; set; }
        public string MaxValueExpression { get; set; }
        public float MaxValue { get; set; }
        public bool UsesPerSchoolBreakdown { get; set; }
        public List<DamageSchoolBreakdownPreview> ShieldBreakdown { get; } = new();
        public string RedirectExpression { get; set; }
        public float RedirectRatio { get; set; }
        public ConditionTarget RedirectTarget { get; set; }
        public string ReflectRatioExpression { get; set; }
        public float ReflectRatio { get; set; }
        public string ReflectFlatExpression { get; set; }
        public float ReflectFlatValue { get; set; }
        public DamageSchool ReflectSchool { get; set; }
        public bool ReflectUsesIncomingDamage { get; set; }
        public ImmunityScope ImmunityScope { get; set; }
        public List<string> ImmuneSkillIDs { get; } = new();
    }

    public class RandomOutcomePreview
    {
        public int RollCount { get; set; }
        public bool AllowDuplicates { get; set; }
        public List<RandomOutcomeOptionPreview> Options { get; } = new();
        public EffectCondition Condition { get; set; }
    }

    public class RepeatEffectPreview
    {
        public RepeatCountSource CountSource { get; set; }
        public int Count { get; set; }
        public int MaxCount { get; set; }
        public string CountExpression { get; set; }
        public ResourceType ResourceType { get; set; }
        public bool ConsumeResource { get; set; }
        public EffectCondition Condition { get; set; }
        public EffectInterpretationResult Result { get; set; }
    }

    public class ProbabilityModifierPreview
    {
        public ProbabilityModifierMode Mode { get; set; }
        public EffectCondition Condition { get; set; }
        public TargetType Target { get; set; }
    }

    public class DotHotModifierPreview
    {
        public DotHotOperation Operation { get; set; }
        public int BaseTriggerCount { get; set; }
        public string ValueExpression { get; set; }
        public float EvaluatedValue { get; set; }
        public int Duration { get; set; }
        public DamageSchool DamageSchool { get; set; }
        public bool CanCrit { get; set; }
        public TargetType Target { get; set; }
        public float Probability { get; set; }
        public EffectCondition Condition { get; set; }
        public EffectInterpretationResult AdditionalEffects { get; set; }
        public bool SupportsStacks { get; set; }
        public int MaxStacks { get; set; }
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