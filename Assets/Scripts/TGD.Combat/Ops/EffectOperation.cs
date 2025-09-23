using System;
using System.Collections.Generic;
using UnityEngine;
using TGD.Data;

namespace TGD.Combat
{
    public enum EffectOpType
    {
        DealDamage,
        Heal,
        ModifyResource,
        ApplyStatus,
        RemoveStatus,
        ModifyCooldown,
        ModifySkill,
        ReplaceSkill,
        Move,
        SpawnAura,
        Schedule,
        Log
    }

    public abstract class EffectOp
    {
        protected EffectOp(EffectOpType type)
        {
            Type = type;
        }

        public EffectOpType Type { get; }
        public float Probability { get; init; } = 100f;
        public EffectCondition Condition { get; init; } = EffectCondition.None;
    }

    public sealed class DealDamageOp : EffectOp
    {
        public DealDamageOp() : base(EffectOpType.DealDamage)
        {
        }

        public Unit Source { get; init; }
        public Unit Target { get; init; }
        public float Amount { get; init; }
        public DamageSchool School { get; init; } = DamageSchool.Physical;
        public bool CanCrit { get; init; }
        public string Expression { get; init; }
        public ImmunityScope ImmunityScope { get; init; } = ImmunityScope.All;
        public float ExpectedNormalDamage { get; init; }
        public float ExpectedCriticalDamage { get; init; }
        public float ExpectedThreat { get; init; }
        public float ExpectedCriticalThreat { get; init; }
        public float ExpectedShred { get; init; }
        public float ExpectedCriticalShred { get; init; }
        public float CriticalMultiplier { get; init; }
        public float AttributeScalingMultiplier { get; init; }
    }

    public sealed class HealOp : EffectOp
    {
        public HealOp() : base(EffectOpType.Heal)
        {
        }

        public Unit Source { get; init; }
        public Unit Target { get; init; }
        public float Amount { get; init; }
        public bool CanCrit { get; init; }
        public string Expression { get; init; }
    }

    public sealed class ModifyResourceOp : EffectOp
    {
        public ModifyResourceOp() : base(EffectOpType.ModifyResource)
        {
        }

        public ResourceType Resource { get; init; } = ResourceType.HP;
        public Unit Target { get; init; }
        public float Amount { get; init; }
        public string Expression { get; init; }
        public ResourceModifyType ModifyType { get; init; }
        public bool FillToMax { get; init; }
        public bool AffectsMax { get; init; }
        public bool StateEnabled { get; init; } = true;
    }

    public sealed class StatusAccumulatorOpConfig
    {
        public StatusAccumulatorSource Source { get; init; }
        public StatusAccumulatorContributor From { get; init; }
        public StatusAccumulatorAmount Amount { get; init; }
        public bool IncludeDotHot { get; init; }
        public DamageSchool? DamageSchool { get; init; }
        public int WindowSeconds { get; init; }
        public string VariableKey { get; init; }
    }

    public sealed class ApplyStatusOp : EffectOp
    {
        public ApplyStatusOp() : base(EffectOpType.ApplyStatus)
        {
        }

        public Unit Source { get; init; }
        public IReadOnlyList<Unit> Targets { get; init; } = Array.Empty<Unit>();
        public TargetType TargetType { get; init; } = TargetType.Self;
        public string StatusSkillId { get; init; }
        public int DurationSeconds { get; init; }
        public bool IsPermanent { get; init; }
        public bool IsInstant { get; init; }
        public int StackCount { get; init; }
        public int MaxStacks { get; init; }
        public StatusAccumulatorOpConfig Accumulator { get; init; }
        public IReadOnlyList<EffectOp> InstantOperations { get; init; } = Array.Empty<EffectOp>();
    }

    public sealed class StatusReplacementSpec
    {
        public string NewStatusSkillId { get; init; }
        public StatusTransferFlags TransferFlags { get; init; } = StatusTransferFlags.None;
        public bool ClampToNewMax { get; init; } = true;
    }

    public enum StatusRemovalMode
    {
        RemoveMatching,
        RemoveAllStacks,
        RemoveSpecificStacks
    }

    public sealed class RemoveStatusOp : EffectOp
    {
        public RemoveStatusOp() : base(EffectOpType.RemoveStatus)
        {
        }

        public TargetType TargetType { get; init; } = TargetType.Self;
        public IReadOnlyList<Unit> Targets { get; init; } = Array.Empty<Unit>();
        public IReadOnlyList<string> StatusSkillIds { get; init; } = Array.Empty<string>();
        public StatusRemovalMode RemovalMode { get; init; } = StatusRemovalMode.RemoveMatching;
        public int StackCount { get; init; }
        public bool ShowStacks { get; init; }
        public int MaxStacks { get; init; }
        public StatusReplacementSpec Replacement { get; init; }
    }

    public sealed class ModifyCooldownOp : EffectOp
    {
        public ModifyCooldownOp() : base(EffectOpType.ModifyCooldown)
        {
        }

        public CooldownTargetScope Scope { get; init; } = CooldownTargetScope.Self;
        public string SkillId { get; init; }
        public int DeltaSeconds { get; init; }
    }

    public sealed class ModifySkillOp : EffectOp
    {
        public ModifySkillOp() : base(EffectOpType.ModifySkill)
        {
        }

        public string TargetSkillId { get; init; }
        public SkillModifyType ModifyType { get; init; } = SkillModifyType.None;
        public SkillModifyOperation Operation { get; init; } = SkillModifyOperation.Minus;
        public ModifierType ModifierType { get; init; } = ModifierType.Flat;
        public string ValueExpression { get; init; }
        public bool AffectAllCosts { get; init; }
        public CostResourceType CostResource { get; init; } = CostResourceType.Energy;
        public bool LimitEnabled { get; init; }
        public string LimitExpression { get; init; }
        public float LimitValue { get; init; }
        public IReadOnlyList<string> IncludeTags { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ExcludeTags { get; init; } = Array.Empty<string>();
        public string SourceHandle { get; init; }
    }

    public sealed class ReplaceSkillOp : EffectOp
    {
        public ReplaceSkillOp() : base(EffectOpType.ReplaceSkill)
        {
        }

        public string TargetSkillId { get; init; }
        public string NewSkillId { get; init; }
        public bool InheritCooldown { get; init; }
    }

    public sealed class MoveOp : EffectOp
    {
        public MoveOp() : base(EffectOpType.Move)
        {
        }

        public MoveSubject Subject { get; init; } = MoveSubject.Caster;
        public MoveExecution Execution { get; init; } = MoveExecution.Step;
        public MoveDirection Direction { get; init; } = MoveDirection.Forward;
        public int Distance { get; init; }
        public string DistanceExpression { get; init; }
        public int MaxDistance { get; init; }
        public Vector2Int Offset { get; init; }
        public bool ForceMovement { get; init; }
        public bool AllowPartialMove { get; init; }
        public bool IgnoreObstacles { get; init; }
        public bool StopAdjacentToTarget { get; init; }
        public TargetType TargetType { get; init; }
    }

    public sealed class AuraOp : EffectOp
    {
        public AuraOp() : base(EffectOpType.SpawnAura)
        {
        }

        public Unit AnchorUnit { get; set; }    // ¡û ÐÂ×Ö¶Î

        public TargetType Source { get; init; } = TargetType.Self;
        public AuraRangeMode RangeMode { get; init; } = AuraRangeMode.Within;
        public float Radius { get; init; }
        public float MinRadius { get; init; }
        public float MaxRadius { get; init; }
        public AuraEffectCategory Category { get; init; } = AuraEffectCategory.Buff;
        public TargetType AffectedTargets { get; init; } = TargetType.All;
        public bool AffectsImmune { get; init; }
        public int DurationSeconds { get; init; }
        public int HeartbeatSeconds { get; init; }
        public EffectCondition OnEnterCondition { get; init; } = EffectCondition.None;
        public EffectCondition OnExitCondition { get; init; } = EffectCondition.None;
        public IReadOnlyList<EffectOp> AdditionalOperations { get; init; } = Array.Empty<EffectOp>();
    }

    public enum ScheduleKind
    {
        Immediate,
        StatusInstant,
        AuraHeartbeat,
        RandomOutcome,
        Repeat,
        DotHotAdditional,
        Custom
    }

    public sealed class ScheduleOption
    {
        public string Label { get; init; }
        public string Description { get; init; }
        public float Probability { get; init; }
        public int Weight { get; init; } = 1;
        public ProbabilityModifierMode ProbabilityMode { get; init; } = ProbabilityModifierMode.None;
        public IReadOnlyList<EffectOp> Operations { get; init; } = Array.Empty<EffectOp>();
    }

    public sealed class ScheduleOp : EffectOp
    {
        public ScheduleOp() : base(EffectOpType.Schedule)
        {
        }

        public ScheduleKind Kind { get; init; } = ScheduleKind.Custom;
        public float DelaySeconds { get; init; }
        public int RepeatCount { get; init; }
        public int MaxCount { get; init; }
        public bool AllowDuplicates { get; init; }
        public RepeatCountSource RepeatSource { get; init; } = RepeatCountSource.Fixed;
        public ResourceType RepeatResourceType { get; init; } = ResourceType.Discipline;
        public bool ConsumeResource { get; init; }
        public IReadOnlyList<EffectOp> Operations { get; init; } = Array.Empty<EffectOp>();
        public IReadOnlyList<ScheduleOption> Options { get; init; } = Array.Empty<ScheduleOption>();
    }

    public sealed class LogOp : EffectOp
    {
        public LogOp() : base(EffectOpType.Log)
        {
        }

        public string Message { get; init; }
    }
}