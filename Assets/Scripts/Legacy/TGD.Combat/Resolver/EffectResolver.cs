using System;
using System.Collections.Generic;
using System.Linq;
using TGD.Data;
using Unity.VisualScripting.YamlDotNet.Core.Tokens;
using static UnityEngine.EventSystems.EventTrigger;

namespace TGD.Combat
{
    public static class EffectResolver
    {
        public static IReadOnlyList<EffectOp> Resolve(EffectInterpretationResult preview, EffectContext context)
        {
            if (preview == null)
                throw new ArgumentNullException(nameof(preview));

            var consumed = new HashSet<object>();
            PreMarkNestedEntries(preview, consumed);
            return ResolveInternal(preview, context, consumed, respectConsumed: true);
        }

        private static IReadOnlyList<EffectOp> ResolveInternal(EffectInterpretationResult preview, EffectContext context, HashSet<object> consumed, bool respectConsumed)
        {
            var operations = new List<EffectOp>();
            AppendDamage(preview, operations, consumed, respectConsumed);
            AppendHealing(preview, operations, consumed, respectConsumed);
            AppendResourceChanges(preview, operations, consumed, respectConsumed);
            AppendStatusApplications(preview, context, operations, consumed, respectConsumed);
            AppendStatusModifications(preview, context, operations, consumed, respectConsumed);
            AppendCooldownModifications(preview, operations, consumed, respectConsumed);
            AppendSkillModifications(preview, operations, consumed, respectConsumed);
            AppendSkillReplacements(preview, operations, consumed, respectConsumed);
            AppendMoves(preview, context, operations, consumed, respectConsumed);
            AppendAuras(preview, context, operations, consumed, respectConsumed);
            AppendSchedules(preview, context, operations, consumed, respectConsumed);
            AppendLogs(preview, operations, consumed, respectConsumed);
            return operations;
        }

        private static void AppendDamage(EffectInterpretationResult preview, List<EffectOp> operations, HashSet<object> consumed, bool respectConsumed)
        {
            foreach (var entry in preview.Damage)
            {
                if (entry == null)
                    continue;
                if (respectConsumed && consumed.Contains(entry))
                    continue;

                var op = new DealDamageOp
                {
                    Source = entry.Source,
                    Target = entry.Target,
                    Amount = entry.Amount,
                    School = entry.School,
                    CanCrit = entry.CanCrit,
                    Expression = entry.Expression,
                    Probability = entry.Probability,
                    Condition = entry.Condition,
                    ConditionNegated = entry.ConditionNegated,
                    ImmunityScope = entry.ImmunityScope,
                    ExpectedNormalDamage = entry.ExpectedNormalDamage,
                    ExpectedCriticalDamage = entry.ExpectedCriticalDamage,
                    ExpectedThreat = entry.ExpectedThreat,
                    ExpectedCriticalThreat = entry.ExpectedCriticalThreat,
                    ExpectedShred = entry.ExpectedShred,
                    ExpectedCriticalShred = entry.ExpectedCriticalShred,
                    CriticalMultiplier = entry.CriticalMultiplier,
                    AttributeScalingMultiplier = entry.AttributeScalingMultiplier
                };
                operations.Add(op);
            }
        }

        private static void AppendHealing(EffectInterpretationResult preview, List<EffectOp> operations, HashSet<object> consumed, bool respectConsumed)
        {
            foreach (var entry in preview.Healing)
            {
                if (entry == null)
                    continue;
                if (respectConsumed && consumed.Contains(entry))
                    continue;

                operations.Add(new HealOp
                {
                    Source = entry.Source,
                    Target = entry.Target,
                    Amount = entry.Amount,
                    CanCrit = entry.CanCrit,
                    Expression = entry.Expression,
                    Probability = entry.Probability,
                    Condition = entry.Condition,
                    ConditionNegated = entry.ConditionNegated
                });
            }
        }

        private static void AppendResourceChanges(EffectInterpretationResult preview, List<EffectOp> operations, HashSet<object> consumed, bool respectConsumed)
        {
            foreach (var entry in preview.ResourceChanges)
            {
                if (entry == null)
                    continue;
                if (respectConsumed && consumed.Contains(entry))
                    continue;

                operations.Add(new ModifyResourceOp
                {
                    Resource = entry.Resource,
                    Target = entry.Target,
                    Amount = entry.Amount,
                    Expression = entry.Expression,
                    ModifyType = entry.ModifyType,
                    FillToMax = entry.FillToMax,
                    AffectsMax = entry.AffectsMax,
                    StateEnabled = entry.StateEnabled,
                    Probability = entry.Probability,
                    Condition = entry.Condition
                });
            }
        }

        private static void AppendStatusApplications(EffectInterpretationResult preview, EffectContext context, List<EffectOp> operations, HashSet<object> consumed, bool respectConsumed)
        {
            foreach (var entry in preview.StatusApplications)
            {
                if (entry == null)
                    continue;
                if (respectConsumed && consumed.Contains(entry))
                    continue;

                var accumulator = entry.Accumulator != null
                    ? new StatusAccumulatorOpConfig
                    {
                        Source = entry.Accumulator.Source,
                        From = entry.Accumulator.From,
                        Amount = entry.Accumulator.Amount,
                        IncludeDotHot = entry.Accumulator.IncludeDotHot,
                        DamageSchool = entry.Accumulator.DamageSchool,
                        WindowSeconds = entry.Accumulator.WindowSeconds,
                        VariableKey = entry.Accumulator.VariableKey
                    }
                    : null;

                IReadOnlyList<EffectOp> instantOps = Array.Empty<EffectOp>();
                if (entry.InstantResult != null)
                    instantOps = ResolveInternal(entry.InstantResult, context, consumed, respectConsumed: false);

                var op = new ApplyStatusOp
                {
                    Source = context?.Caster,
                    Targets = ResolveTargets(entry.Target, context),
                    TargetType = entry.Target,
                    StatusSkillId = entry.StatusSkillID,
                    DurationSeconds = ConvertDurationToSeconds(entry.Duration, entry.IsInstant, entry.IsPermanent),
                    IsPermanent = entry.IsPermanent,
                    IsInstant = entry.IsInstant,
                    StackCount = entry.StackCount,
                    MaxStacks = entry.MaxStacks,
                    Probability = entry.Probability,
                    Condition = entry.Condition,
                    ConditionNegated = entry.ConditionNegated,
                    Accumulator = accumulator,
                    InstantOperations = instantOps
                };

                operations.Add(op);
            }
        }

        private static void AppendStatusModifications(EffectInterpretationResult preview, EffectContext context, List<EffectOp> operations, HashSet<object> consumed, bool respectConsumed)
        {
            foreach (var entry in preview.StatusModifications)
            {
                if (entry == null)
                    continue;
                if (respectConsumed && consumed.Contains(entry))
                    continue;

                if (entry.ModifyType == StatusModifyType.ApplyStatus)
                    continue;

                var removalMode = DetermineRemovalMode(entry);
                var replacement = entry.ModifyType == StatusModifyType.ReplaceStatus &&
                                  !string.IsNullOrWhiteSpace(entry.ReplacementSkillID)
                    ? new StatusReplacementSpec
                    {
                        NewStatusSkillId = entry.ReplacementSkillID,
                        TransferFlags = entry.TransferFlags,
                        ClampToNewMax = entry.ClampToNewMax
                    }
                    : null;

                var op = new RemoveStatusOp
                {
                    TargetType = entry.Target,
                    Targets = ResolveTargets(entry.Target, context),
                    StatusSkillIds = entry.SkillIDs?.Where(NotNullOrWhiteSpace).ToArray() ?? Array.Empty<string>(),
                    RemovalMode = removalMode,
                    StackCount = entry.StackCount,
                    ShowStacks = entry.ShowStacks,
                    MaxStacks = entry.MaxStacks,
                    Replacement = replacement,
                    Probability = entry.Probability,
                    Condition = entry.Condition,
                    ConditionNegated = entry.ConditionNegated
                };

                operations.Add(op);
            }
        }

        private static StatusRemovalMode DetermineRemovalMode(StatusModificationPreview entry)
        {
            if (entry.ShowStacks)
            {
                if (entry.StackCount <= 0)
                    return StatusRemovalMode.RemoveAllStacks;
                return StatusRemovalMode.RemoveSpecificStacks;
            }

            return StatusRemovalMode.RemoveMatching;
        }

        private static void AppendCooldownModifications(EffectInterpretationResult preview, List<EffectOp> operations, HashSet<object> consumed, bool respectConsumed)
        {
            foreach (var entry in preview.CooldownModifications)
            {
                if (entry == null)
                    continue;
                if (respectConsumed && consumed.Contains(entry))
                    continue;

                operations.Add(new ModifyCooldownOp
                {
                    Scope = entry.Scope,
                    SkillId = entry.SelfSkillID,
                    DeltaSeconds = ResolveCooldownSeconds(entry),
                    Probability = entry.Probability,
                    Condition = entry.Condition,
                    ConditionNegated = entry.ConditionNegated
                });
            }
        }

        private static int ResolveCooldownSeconds(CooldownModificationPreview entry)
        {
            int seconds = entry.Seconds;
            if (entry.Turns != 0)
                seconds += entry.Turns * CombatClock.BaseTurnSeconds;
            if (entry.Rounds != 0)
                seconds += entry.Rounds * CombatClock.BaseTurnSeconds * 2;
            return seconds;
        }

        private static void AppendSkillModifications(EffectInterpretationResult preview, List<EffectOp> operations, HashSet<object> consumed, bool respectConsumed)
        {
            foreach (var entry in preview.SkillModifications)
            {
                if (entry == null)
                    continue;
                if (respectConsumed && consumed.Contains(entry))
                    continue;

                operations.Add(new ModifySkillOp
                {
                    TargetSkillId = entry.TargetSkillID,
                    ModifyType = entry.ModifyType,
                    Operation = entry.Operation,
                    ModifierType = entry.ModifierType,
                    ValueExpression = entry.ValueExpression,
                    AffectAllCosts = entry.AffectAllCosts,
                    CostResource = entry.CostResource,
                    LimitEnabled = entry.LimitEnabled,
                    LimitExpression = entry.LimitExpression,
                    LimitValue = entry.LimitValue,
                    IncludeTags = entry.IncludeTags?.Where(NotNullOrWhiteSpace).ToArray() ?? Array.Empty<string>(),
                    ExcludeTags = entry.ExcludeTags?.Where(NotNullOrWhiteSpace).ToArray() ?? Array.Empty<string>(),
                    SourceHandle = entry.SourceHandle,
                    Probability = entry.Probability,
                    Condition = entry.Condition,
                    ConditionNegated = entry.ConditionNegated
                });
            }
        }

        private static bool NotNullOrWhiteSpace(string value) => !string.IsNullOrWhiteSpace(value);

        private static void AppendSkillReplacements(EffectInterpretationResult preview, List<EffectOp> operations, HashSet<object> consumed, bool respectConsumed)
        {
            foreach (var entry in preview.SkillReplacements)
            {
                if (entry == null)
                    continue;
                if (respectConsumed && consumed.Contains(entry))
                    continue;

                operations.Add(new ReplaceSkillOp
                {
                    TargetSkillId = entry.TargetSkillID,
                    NewSkillId = entry.NewSkillID,
                    InheritCooldown = entry.InheritCooldown,
                    Probability = entry.Probability,
                    Condition = entry.Condition,
                    ConditionNegated = entry.ConditionNegated
                });
            }
        }

        private static void AppendMoves(EffectInterpretationResult preview, EffectContext context, List<EffectOp> operations, HashSet<object> consumed, bool respectConsumed)
        {
            foreach (var entry in preview.Moves)
            {
                if (entry == null)
                    continue;
                if (respectConsumed && consumed.Contains(entry))
                    continue;

                operations.Add(new MoveOp
                {
                    Subject = entry.Subject,
                    Execution = entry.Execution,
                    Direction = entry.Direction,
                    Distance = entry.Distance,
                    DistanceExpression = entry.DistanceExpression,
                    MaxDistance = entry.MaxDistance,
                    Offset = entry.Offset,
                    ForceMovement = entry.ForceMovement,
                    AllowPartialMove = entry.AllowPartialMove,
                    IgnoreObstacles = entry.IgnoreObstacles,
                    StopAdjacentToTarget = entry.StopAdjacentToTarget,
                    TargetType = entry.Target,
                    DurationSeconds = entry.DurationSeconds,
                    Probability = entry.Probability,
                    Condition = entry.Condition,
                    ConditionNegated = entry.ConditionNegated
                });
            }
        }

        private static void AppendAuras(EffectInterpretationResult preview, EffectContext context, List<EffectOp> operations, HashSet<object> consumed, bool respectConsumed)
        {
            if (preview?.Auras == null)
                return;

            foreach (var entry in preview.Auras)
            {
                if (entry == null)
                    continue;
                if (respectConsumed && consumed.Contains(entry))
                    continue;

                var additionalOps = entry.AdditionalEffects != null
                    ? ResolveInternal(entry.AdditionalEffects, context, consumed, respectConsumed: false)
                    : Array.Empty<EffectOp>();

                var anchor = ResolveAuraAnchor(entry.Source, context);

                operations.Add(new AuraOp
                {
                    AnchorUnit = anchor,
                    Source = entry.Source,
                    RangeMode = entry.RangeMode,
                    Radius = entry.Radius,
                    MinRadius = entry.MinRadius,
                    MaxRadius = entry.MaxRadius,
                    Category = entry.Category,
                    AffectedTargets = entry.AffectedTargets,
                    AffectsImmune = entry.AffectsImmune,
                    DurationSeconds = ConvertDurationToSeconds(entry.Duration, false, entry.Duration <= 0),
                    HeartbeatSeconds = entry.HeartbeatSeconds,
                    OnEnterCondition = entry.OnEnterCondition,
                    OnExitCondition = entry.OnExitCondition,
                    AdditionalOperations = additionalOps,
                    Probability = entry.Probability,
                    Condition = entry.Condition,
                    ConditionNegated = entry.ConditionNegated
                });
            }
        }

        private static Unit ResolveAuraAnchor(TargetType source, EffectContext context)
        {
            if (context == null)
                return null;

            return source switch
            {
                TargetType.Self => context.Caster,
                TargetType.Enemy => context.PrimaryTarget,
                TargetType.Allies => context.Allies.FirstOrDefault(u => u != null),
                TargetType.All => context.PrimaryTarget ?? context.Caster,
                _ => context.Caster
            };
        }

        private static void AppendSchedules(EffectInterpretationResult preview, EffectContext context, List<EffectOp> operations, HashSet<object> consumed, bool respectConsumed)
        {
            foreach (var repeat in preview.RepeatEffects)
            {
                if (repeat == null)
                    continue;
                if (respectConsumed && consumed.Contains(repeat))
                    continue;

                var repeatOps = repeat.Result != null
                    ? ResolveInternal(repeat.Result, context, consumed, respectConsumed: false)
                    : Array.Empty<EffectOp>();

                operations.Add(new ScheduleOp
                {
                    Kind = ScheduleKind.Repeat,
                    RepeatSource = repeat.CountSource,
                    RepeatCount = repeat.Count,
                    MaxCount = repeat.MaxCount,
                    RepeatResourceType = repeat.ResourceType,
                    ConsumeResource = repeat.ConsumeResource,
                    Operations = repeatOps,
                    Probability = 100f,
                    Condition = repeat.Condition,
                    ConditionNegated = repeat.ConditionNegated
                });
            }

            foreach (var random in preview.RandomOutcomes)
            {
                if (random == null)
                    continue;
                if (respectConsumed && consumed.Contains(random))
                    continue;

                var options = new List<ScheduleOption>();
                foreach (var option in random.Options)
                {
                    if (option == null)
                        continue;
                    if (respectConsumed && consumed.Contains(option))
                        continue;

                    var optionOps = option.Result != null
                        ? ResolveInternal(option.Result, context, consumed, respectConsumed: false)
                        : Array.Empty<EffectOp>();

                    options.Add(new ScheduleOption
                    {
                        Label = option.Label,
                        Description = option.Description,
                        Probability = option.Probability,
                        Weight = option.Weight,
                        ProbabilityMode = option.ProbabilityMode,
                        Operations = optionOps
                    });
                }

                operations.Add(new ScheduleOp
                {
                    Kind = ScheduleKind.RandomOutcome,
                    Options = options,
                    RepeatCount = random.RollCount,
                    AllowDuplicates = random.AllowDuplicates,
                    Probability = 100f,
                    Condition = random.Condition,
                    ConditionNegated = random.ConditionNegated
                });
            }

            foreach (var dotHot in preview.DotHotModifiers)
            {
                if (dotHot == null || dotHot.AdditionalEffects == null)
                    continue;
                if (respectConsumed && consumed.Contains(dotHot))
                    continue;

                var nested = ResolveInternal(dotHot.AdditionalEffects, context, consumed, respectConsumed: false);
                if (nested.Count == 0)
                    continue;

                operations.Add(new ScheduleOp
                {
                    Kind = ScheduleKind.DotHotAdditional,
                    Operations = nested,
                    Probability = dotHot.Probability,
                    Condition = dotHot.Condition,
                    ConditionNegated = dotHot.ConditionNegated
                });
            }
        }

        private static void AppendLogs(EffectInterpretationResult preview, List<EffectOp> operations, HashSet<object> consumed, bool respectConsumed)
        {
            foreach (var message in preview.Logs)
            {
                if (string.IsNullOrWhiteSpace(message))
                    continue;
                if (respectConsumed && consumed.Contains(message))
                    continue;

                operations.Add(new LogOp
                {
                    Message = message
                });
            }
        }

        private static void PreMarkNestedEntries(EffectInterpretationResult preview, HashSet<object> consumed)
        {
            if (preview == null)
                return;

            foreach (var status in preview.StatusApplications)
            {
                if (status?.InstantResult == null)
                    continue;

                MarkPreviewEntries(status.InstantResult, consumed);
                PreMarkNestedEntries(status.InstantResult, consumed);
            }

            foreach (var aura in preview.Auras)
            {
                if (aura?.AdditionalEffects == null)
                    continue;

                MarkPreviewEntries(aura.AdditionalEffects, consumed);
                PreMarkNestedEntries(aura.AdditionalEffects, consumed);
            }

            foreach (var repeat in preview.RepeatEffects)
            {
                if (repeat?.Result == null)
                    continue;

                MarkPreviewEntries(repeat.Result, consumed);
                PreMarkNestedEntries(repeat.Result, consumed);
            }

            foreach (var random in preview.RandomOutcomes)
            {
                if (random == null)
                    continue;

                foreach (var option in random.Options)
                {
                    if (option?.Result == null)
                        continue;

                    MarkPreviewEntries(option.Result, consumed);
                    PreMarkNestedEntries(option.Result, consumed);
                }
            }

            foreach (var dotHot in preview.DotHotModifiers)
            {
                if (dotHot?.AdditionalEffects == null)
                    continue;

                MarkPreviewEntries(dotHot.AdditionalEffects, consumed);
                PreMarkNestedEntries(dotHot.AdditionalEffects, consumed);
            }
        }

        private static void MarkPreviewEntries(EffectInterpretationResult preview, HashSet<object> consumed)
        {
            if (preview == null)
                return;

            foreach (var item in preview.Damage)
                if (item != null)
                    consumed.Add(item);
            foreach (var item in preview.Healing)
                if (item != null)
                    consumed.Add(item);
            foreach (var item in preview.ResourceChanges)
                if (item != null)
                    consumed.Add(item);
            foreach (var item in preview.StatusApplications)
                if (item != null)
                    consumed.Add(item);
            foreach (var item in preview.StatusModifications)
                if (item != null)
                    consumed.Add(item);
            foreach (var item in preview.CooldownModifications)
                if (item != null)
                    consumed.Add(item);
            foreach (var item in preview.SkillModifications)
                if (item != null)
                    consumed.Add(item);
            foreach (var item in preview.SkillReplacements)
                if (item != null)
                    consumed.Add(item);
            foreach (var item in preview.Moves)
                if (item != null)
                    consumed.Add(item);
            foreach (var item in preview.Auras)
                if (item != null)
                    consumed.Add(item);
            foreach (var item in preview.RandomOutcomes)
            {
                if (item == null)
                    continue;
                consumed.Add(item);
                foreach (var option in item.Options)
                    if (option != null)
                        consumed.Add(option);
            }
            foreach (var item in preview.RepeatEffects)
                if (item != null)
                    consumed.Add(item);
            foreach (var item in preview.DotHotModifiers)
                if (item != null)
                    consumed.Add(item);
            foreach (var item in preview.Logs)
                if (!string.IsNullOrWhiteSpace(item))
                    consumed.Add(item);
        }

        private static IReadOnlyList<Unit> ResolveTargets(TargetType targetType, EffectContext context)
        {
            if (context == null)
                return Array.Empty<Unit>();

            var targets = new List<Unit>();
            switch (targetType)
            {
                case TargetType.Self:
                    if (context.Caster != null)
                        targets.Add(context.Caster);
                    break;
                case TargetType.Enemy:
                    if (context.PrimaryTarget != null)
                        targets.Add(context.PrimaryTarget);
                    break;
                case TargetType.Allies:
                    if (context.Allies.Count > 0)
                        targets.AddRange(context.Allies);
                    else if (context.Caster != null)
                        targets.Add(context.Caster);
                    break;
                case TargetType.All:
                    var unique = new HashSet<Unit>();
                    if (context.Caster != null) unique.Add(context.Caster);
                    if (context.PrimaryTarget != null) unique.Add(context.PrimaryTarget);
                    foreach (var ally in context.Allies) unique.Add(ally);
                    foreach (var enemy in context.Enemies) unique.Add(enemy);
                    targets.AddRange(unique);
                    break;
            }

            return targets;
        }

        private static int ConvertDurationToSeconds(int duration, bool isInstant, bool isPermanent)
        {
            if (isInstant)
                return 0;
            if (isPermanent)
                return -1;
            if (duration <= 0)
                return 0;
            return duration * CombatClock.BaseTurnSeconds;
        }
    }
}