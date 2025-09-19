using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using TGD.Core;
using TGD.Data;

namespace TGD.Combat
{
    public static class EffectInterpreter
    {
        public static EffectInterpretationResult InterpretSkill(Unit caster, SkillDefinition skill, EffectContext context = null)
        {
            if (skill == null) throw new ArgumentNullException(nameof(skill));

            EffectContext workingContext = context != null
                ? context.CloneForSkill(skill, inheritSkillLevelOverride: true)
                : new EffectContext(caster, skill);

            if (workingContext.Caster == null && caster != null)
            {
                workingContext = new EffectContext(caster, skill)
                {
                    IncomingDamage = workingContext.IncomingDamage,
                    IncomingDamageMitigated = workingContext.IncomingDamageMitigated,
                    ConditionAfterAttack = workingContext.ConditionAfterAttack,
                    ConditionOnCrit = workingContext.ConditionOnCrit,
                    ConditionOnCooldownEnd = workingContext.ConditionOnCooldownEnd,
                    ConditionAfterSkillUse = workingContext.ConditionAfterSkillUse,
                    LastSkillUsedID = workingContext.LastSkillUsedID,
                    ConditionSkillStateActive = workingContext.ConditionSkillStateActive,
                    ConditionOnResourceSpend = workingContext.ConditionOnResourceSpend,
                    ConditionOnEffectEnd = workingContext.ConditionOnEffectEnd,
                    ConditionOnDamageTaken = workingContext.ConditionOnDamageTaken,
                    LastResourceSpendAmount = workingContext.LastResourceSpendAmount,
                    LastResourceSpendType = workingContext.LastResourceSpendType,
                    SkillResolver = workingContext.SkillResolver,
                    PrimaryTarget = workingContext.PrimaryTarget,
                    SecondaryTarget = workingContext.SecondaryTarget
                };

                workingContext.Allies.Clear();
                workingContext.Allies.AddRange(context?.Allies ?? new List<Unit> { caster });
                workingContext.Enemies.Clear();
                workingContext.Enemies.AddRange(context?.Enemies ?? new List<Unit>());

                workingContext.ResourceValues.Clear();
                foreach (var kvp in context?.ResourceValues ?? new Dictionary<ResourceType, float>())
                    workingContext.ResourceValues[kvp.Key] = kvp.Value;

                workingContext.ResourceMaxValues.Clear();
                foreach (var kvp in context?.ResourceMaxValues ?? new Dictionary<ResourceType, float>())
                    workingContext.ResourceMaxValues[kvp.Key] = kvp.Value;

                workingContext.ResourceSpent.Clear();
                foreach (var kvp in context?.ResourceSpent ?? new Dictionary<ResourceType, float>())
                    workingContext.ResourceSpent[kvp.Key] = kvp.Value;

                workingContext.CustomVariables.Clear();
                foreach (var kvp in context?.CustomVariables ?? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase))
                    workingContext.CustomVariables[kvp.Key] = kvp.Value;

                workingContext.ActiveSkillStates.Clear();
                if (context != null)
                {
                    foreach (var state in context.ActiveSkillStates)
                        workingContext.ActiveSkillStates.Add(state);
                }

                workingContext.ActiveDotHotStatuses.Clear();
                if (context != null)
                {
                    foreach (var status in context.ActiveDotHotStatuses)
                    {
                        if (status == null)
                            continue;
                        workingContext.ActiveDotHotStatuses.Add(new DotHotStatusSnapshot
                        {
                            SkillID = status.SkillID,
                            Target = status.Target,
                            Stacks = status.Stacks,
                            IsHot = status.IsHot
                        });
                    }
                }

                workingContext.ConditionDotStacks = context?.ConditionDotStacks ?? 0f;

                if (context != null && context.HasSkillLevelOverride)
                    workingContext.OverrideSkillLevel(context.ResolveSkillLevel(context.Skill));
            }

            return InterpretSkillInternal(workingContext, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        public static EffectInterpretationResult InterpretSkill(EffectContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Skill == null) throw new ArgumentException("EffectContext must include a Skill.", nameof(context));

            var working = context.CloneForSkill(context.Skill, inheritSkillLevelOverride: true);
            return InterpretSkillInternal(working, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private static EffectInterpretationResult InterpretSkillInternal(EffectContext context, HashSet<string> visited)
        {
            var result = new EffectInterpretationResult();
            if (context.Skill == null)
                return result;

            string skillId = context.Skill.skillID;
            if (!string.IsNullOrWhiteSpace(skillId))
            {
                if (visited.Contains(skillId))
                {
                    result.AddLog($"Skipped skill '{skillId}' to avoid recursion.");
                    return result;
                }
                visited.Add(skillId);
            }

            if (context.Skill?.useConditions != null)
            {
                foreach (var condition in context.Skill.useConditions)
                {
                    var preview = EvaluateSkillUseCondition(condition, context);
                    result.SkillUseConditions.Add(preview);
                    result.AddLog(BuildSkillUseConditionLog(preview));
                }
            }

            foreach (var effect in context.Skill.effects)
            {
                InterpretEffect(effect, context, result, visited);
            }

            if (!string.IsNullOrWhiteSpace(skillId))
                visited.Remove(skillId);

            return result;
        }
        private static SkillUseConditionPreview EvaluateSkillUseCondition(SkillUseCondition condition, EffectContext context)
        {
            var preview = new SkillUseConditionPreview
            {
                ConditionType = condition?.conditionType ?? SkillCostConditionType.Resource,
                Target = condition?.target ?? ConditionTarget.Caster,
                Resource = condition?.resourceType ?? ResourceType.Discipline,
                Comparison = condition?.compareOp ?? CompareOp.Equal,
                CompareValue = condition?.compareValue ?? 0f,
                CompareExpression = condition?.compareValueExpression
            };

            switch (preview.ConditionType)
            {
                case SkillCostConditionType.Resource:
                    {
                        var referenceUnit = ResolveConditionTarget(preview.Target, context);
                        float compareValue = EvaluateConditionValue(condition, context, referenceUnit);
                        preview.CompareValue = compareValue;
                        float maxValue;
                        preview.CurrentValue = ResolveConditionResourceValue(condition, context, referenceUnit, out maxValue);
                        preview.MaxValue = maxValue;
                        preview.Succeeded = EvaluateComparison(preview.CurrentValue, compareValue, preview.Comparison);
                        break;
                    }
                case SkillCostConditionType.Distance:
                    {
                        preview.MinDistance = condition?.minDistance ?? 0;
                        preview.MaxDistance = condition?.maxDistance ?? 0;
                        preview.RequireLineOfSight = condition?.requireLineOfSight ?? false;
                        preview.Distance = context?.GetDistance(preview.Target) ?? 0f;
                        preview.PathBlocked = preview.RequireLineOfSight && (context?.IsPathBlocked(preview.Target) ?? false);
                        bool meetsMin = preview.Distance >= preview.MinDistance;
                        bool meetsMax = preview.MaxDistance <= 0 || preview.Distance <= preview.MaxDistance;
                        bool meetsPath = !preview.RequireLineOfSight || !preview.PathBlocked;
                        preview.Succeeded = meetsMin && meetsMax && meetsPath;
                        break;
                    }
                case SkillCostConditionType.PerformHeal:
                    preview.Succeeded = context?.ConditionOnPerformHeal ?? false;
                    break;
                case SkillCostConditionType.PerformAttack:
                    preview.Succeeded = context?.ConditionOnPerformAttack ?? false;
                    break;
            }

            return preview;
        }
        private static string BuildSkillUseConditionLog(SkillUseConditionPreview preview)
        {
            if (preview == null)
                return "Use condition: (invalid).";

            switch (preview.ConditionType)
            {
                case SkillCostConditionType.Resource:
                    {
                        string valueLabel = !string.IsNullOrWhiteSpace(preview.CompareExpression)
                            ? preview.CompareExpression
                            : preview.CompareValue.ToString("0.##", CultureInfo.InvariantCulture);
                        return $"Use condition [Resource] ({preview.Resource}) on {DescribeConditionTarget(preview.Target)} {preview.Comparison} {valueLabel}. Current {preview.CurrentValue:0.##} / Max {preview.MaxValue:0.##} => {(preview.Succeeded ? "met" : "failed")}.";
                    }
                case SkillCostConditionType.Distance:
                    {
                        string maxLabel = preview.MaxDistance > 0 ? preview.MaxDistance.ToString(CultureInfo.InvariantCulture) : "inf";
                        string losLabel = preview.RequireLineOfSight ? (preview.PathBlocked ? "blocked" : "clear") : "ignored";
                        return $"Use condition [Distance] to {DescribeConditionTarget(preview.Target)}: {preview.Distance:0.##} (min {preview.MinDistance}, max {maxLabel}, path {losLabel}) => {(preview.Succeeded ? "met" : "failed")}.";
                    }
                case SkillCostConditionType.PerformHeal:
                    return $"Use condition [Perform Heal] on {DescribeConditionTarget(preview.Target)} => {(preview.Succeeded ? "met" : "failed")}.";
                case SkillCostConditionType.PerformAttack:
                    return $"Use condition [Perform Attack] on {DescribeConditionTarget(preview.Target)} => {(preview.Succeeded ? "met" : "failed")}.";
                default:
                    return "Use condition evaluated.";
            }
        }

        private static Unit ResolveConditionTarget(ConditionTarget target, EffectContext context)
        {
            return target switch
            {
                ConditionTarget.Caster => context?.Caster,
                ConditionTarget.PrimaryTarget => context?.PrimaryTarget,
                ConditionTarget.SecondaryTarget => context?.SecondaryTarget,
                _ => context?.PrimaryTarget ?? context?.Caster
            };
        }

        private static float EvaluateConditionValue(SkillUseCondition condition, EffectContext context, Unit referenceUnit)
        {
            if (condition == null)
                return 0f;

            string expression = condition.compareValueExpression;
            if (!string.IsNullOrWhiteSpace(expression))
                return EvaluateExpression(expression, context, referenceUnit, condition.compareValue);

            return condition.compareValue;
        }

        private static float ResolveConditionResourceValue(SkillUseCondition condition, EffectContext context, Unit referenceUnit, out float maxValue)
        {
            maxValue = 0f;
            if (condition == null)
                return 0f;

            float current = context?.GetResourceAmount(condition.resourceType) ?? 0f;
            maxValue = context?.GetResourceMax(condition.resourceType) ?? 0f;

            var stats = referenceUnit?.Stats;
            if (stats != null)
            {
                switch (condition.resourceType)
                {
                    case ResourceType.HP:
                        current = stats.HP;
                        maxValue = stats.MaxHP;
                        break;
                    case ResourceType.Energy:
                        current = stats.Energy;
                        maxValue = stats.MaxEnergy;
                        break;
                    case ResourceType.posture:
                        current = stats.Posture;
                        maxValue = stats.MaxPosture;
                        break;
                }
            }

            return current;
        }

        private static string DescribeConditionTarget(ConditionTarget target)
        {
            return target switch
            {
                ConditionTarget.Caster => "caster",
                ConditionTarget.PrimaryTarget => "primary target",
                ConditionTarget.SecondaryTarget => "secondary target",
                _ => "target"
            };
        }

        private static bool MatchesConditionTarget(ConditionTarget target, EffectContext context)
        {
            if (target == ConditionTarget.Any || context == null)
                return true;

            var expected = ResolveConditionTarget(target, context);
            var actual = context.ConditionEventTarget ?? context.PrimaryTarget;

            if (expected == null || actual == null)
                return true;

            return ReferenceEquals(expected, actual);
        }
        private static void InterpretEffect(EffectDefinition effect, EffectContext context, EffectInterpretationResult result, HashSet<string> visited)
        {
            if (effect == null)
                return;

            if (!CheckCondition(effect, context))
            {
                result.AddLog($"Condition {effect.condition} not met for {effect.effectType}.");
                return;
            }
            int effectTypeValue = (int)effect.effectType;
  

            switch (effect.effectType)
            {
                case EffectType.Damage:
                    ApplyDamage(effect, context, result);
                    break;
                case EffectType.Heal:
                    ApplyHeal(effect, context, result);
                    break;
                case EffectType.GainResource:
                    ApplyResourceGain(effect, context, result);
                    break;
                case EffectType.ScalingBuff:
                    ApplyScalingBuff(effect, context, result);
                    break;
                case EffectType.ModifyStatus:
                    ApplyModifyStatus(effect, context, result, visited);
                    break;
                case EffectType.ConditionalEffect:
                    ApplyConditional(effect, context, result, visited);
                    break;
                case EffectType.ModifySkill:
                    ApplyModifySkill(effect, context, result);
                    break;
                case EffectType.ReplaceSkill:
                    ApplyReplaceSkill(effect, context, result);
                    break;
                case EffectType.Move:
                    ApplyMove(effect, context, result);
                    break;
                case EffectType.ModifyAction:
                    ApplyModifyAction(effect, context, result);
                    break;
                case EffectType.ModifyDamageSchool:
                    ApplyModifyDamageSchool(effect, context, result);
                    break;
                case EffectType.AttributeModifier:
                    ApplyAttributeModifier(effect, context, result);
                    break;
                case EffectType.MasteryPosture:
                    ApplyMasteryPosture(effect, context, result);
                    break;
                case EffectType.CooldownModifier:
                    ApplyCooldownModifier(effect, context, result);
                    break;
                case EffectType.RandomOutcome:
                    ApplyRandomOutcome(effect, context, result, visited);
                    break;
                case EffectType.Repeat:
                    ApplyRepeat(effect, context, result, visited);
                    break;
                case EffectType.ProbabilityModifier:
                    ApplyProbabilityModifier(effect, context, result);
                    break;
                case EffectType.DotHotModifier:
                    ApplyDotHotModifier(effect, context, result, visited);
                    break;
                default:
                    result.AddLog($"No interpreter implemented for {effect.effectType}.");
                    break;
            }
        }

        private static void ApplyDamage(EffectDefinition effect, EffectContext context, EffectInterpretationResult result)
        {
            var targets = ResolveTargets(effect.target, context);
            if (targets.Count == 0)
                targets.Add(context.PrimaryTarget ?? context.Caster);

            string expression = ResolveValueExpression(effect, context);
            foreach (var target in targets)
            {
                float amount = EvaluateExpression(expression, context, target, effect.value);
                float probability = ResolveProbabilityValue(effect, context, target);
                var preview = new DamagePreview
                {
                    Source = context.Caster,
                    Target = target,
                    Amount = amount,
                    School = effect.damageSchool,
                    CanCrit = effect.canCrit,
                    Probability = probability,
                    Expression = expression,
                    Condition = effect.condition
                };

                PopulateDamagePreview(preview, effect, context, target);
                result.Damage.Add(preview);

                string log = $"Damage {DescribeUnit(target)} for {amount:0.##} {effect.damageSchool} ({probability:0.##}% chance).";
                if (preview.ExpectedNormalDamage > 0f)
                {
                    log += $" Preview: {preview.ExpectedNormalDamage:0.##}";
                    if (preview.CanCrit)
                        log += $" / Crit: {preview.ExpectedCriticalDamage:0.##}";
                }
                if (preview.ExpectedThreat > 0f || preview.ExpectedCriticalThreat > 0f)
                    log += $" | Threat: {preview.ExpectedThreat:0.##}";
                if (preview.ExpectedShred > 0f || preview.ExpectedCriticalShred > 0f)
                    log += $" | Shred: {preview.ExpectedShred:0.##}";

                result.AddLog(log);
            }
        }

        private static void ApplyHeal(EffectDefinition effect, EffectContext context, EffectInterpretationResult result)
        {
            var targets = ResolveTargets(effect.target, context);
            if (targets.Count == 0)
                targets.Add(context.PrimaryTarget ?? context.Caster);

            string expression = ResolveValueExpression(effect, context);
            foreach (var target in targets)
            {
                float amount = EvaluateExpression(expression, context, target, effect.value);
                float probability = ResolveProbabilityValue(effect, context, target);
                result.Healing.Add(new HealPreview
                {
                    Source = context.Caster,
                    Target = target,
                    Amount = amount,
                    CanCrit = effect.canCrit,
                    Probability = probability,
                    Expression = expression,
                    Condition = effect.condition
                });
                result.AddLog($"Heal {DescribeUnit(target)} for {amount:0.##} ({probability:0.##}% chance).");
            }
        }

        private static void ApplyResourceGain(EffectDefinition effect, EffectContext context, EffectInterpretationResult result)
        {
            var targets = ResolveTargets(effect.target, context);
            if (targets.Count == 0)
                targets.Add(context.PrimaryTarget ?? context.Caster);

            string expression = ResolveValueExpression(effect, context);
            bool fillToMax = IsMaxExpression(expression);
            foreach (var target in targets)
            {
                float amount = fillToMax
    ? CalculateMaxFillAmount(effect.resourceType, context, target)
    : EvaluateExpression(expression, context, target, effect.value);
                float probability = ResolveProbabilityValue(effect, context, target);
                result.ResourceChanges.Add(new ResourceChangePreview
                {
                    Resource = effect.resourceType,
                    Target = target,
                    Amount = amount,
                    Probability = probability,
                    Expression = expression,
                    Condition = effect.condition,
                    FillToMax = fillToMax
                });
                if (fillToMax)
                {
                    result.AddLog($"Restore {effect.resourceType} to max for {DescribeUnit(target)} ({probability:0.##}% chance).");
                }
                else
                {
                    result.AddLog($"Gain {amount:0.##} {effect.resourceType} for {DescribeUnit(target)} ({probability:0.##}% chance).");
                }
            }
        }

        private static void ApplyScalingBuff(EffectDefinition effect, EffectContext context, EffectInterpretationResult result)
        {
            float probability = ResolveProbabilityValue(effect, context, context.PrimaryTarget ?? context.Caster);
            result.ScalingBuffs.Add(new ScalingBuffPreview
            {
                Resource = effect.resourceType,
                ValuePerResource = string.IsNullOrWhiteSpace(effect.scalingValuePerResource) ? string.Empty : effect.scalingValuePerResource,
                MaxStacks = effect.maxStacks,
                Attribute = effect.scalingAttribute,
                Operation = effect.scalingOperation,
                Target = effect.target,
                Probability = probability,
                Condition = effect.condition
            });
            string opLabel = effect.scalingOperation.ToString();
            string valueLabel = string.IsNullOrWhiteSpace(effect.scalingValuePerResource)
                ? "(missing value)"
                : effect.scalingValuePerResource;
            result.AddLog($"Scaling buff [{opLabel}]: {effect.scalingAttribute} per {effect.resourceType} ({valueLabel}).");
        }

        private static void InterpretApplyStatus(EffectDefinition effect, EffectContext context, EffectInterpretationResult result, HashSet<string> visited, IReadOnlyList<string> explicitSkillIds = null)
        {
            var skillIds = GatherStatusSkillIds(effect, explicitSkillIds);
            if (skillIds.Count == 0)
            {
                result.AddLog("ModifyStatus apply effect is missing status skill IDs.");
                return;
            }

            foreach (var statusSkillId in skillIds)
            {
                InterpretApplyStatusForSkill(effect, context, result, visited, statusSkillId);
            }
        }

        private static void InterpretApplyStatusForSkill(EffectDefinition effect, EffectContext context, EffectInterpretationResult result, HashSet<string> visited, string statusSkillId)
        {
            var targets = ResolveTargets(effect.target, context);
            float probability = ResolveProbabilityValue(effect, context, targets.Count > 0 ? targets[0] : context.PrimaryTarget);
            int duration = ResolveDuration(effect, context);
            int stacks = ResolveStackCount(effect, context);
            bool isInstant = duration == -1;
            bool isPermanent = duration == -2;

            var preview = new StatusApplicationPreview
            {
                StatusSkillID = statusSkillId,
                Duration = duration,
                StackCount = stacks,
                IsInstant = isInstant,
                IsPermanent = isPermanent,
                MaxStacks = effect.maxStacks,
                Target = effect.target,
                Probability = probability,
                Condition = effect.condition
            };

            if (isInstant && !string.IsNullOrWhiteSpace(statusSkillId) && context.SkillResolver != null)
            {
                var statusSkill = context.SkillResolver.FindSkill(statusSkillId);
                if (statusSkill != null)
                {
                    var childContext = context.CloneForSkill(statusSkill, inheritSkillLevelOverride: false);
                    preview.InstantResult = InterpretSkillInternal(childContext, visited);
                    result.Append(preview.InstantResult);
                }
                else
                {
                    result.AddLog($"Status skill '{statusSkillId}' not found for ModifyStatus apply effect.");
                }
            }

            result.StatusApplications.Add(preview);
            string action = isInstant ? "Trigger" : "Apply";
            
            string stackLabel = stacks > 0 ? $"{stacks} stack(s)" : "(no stacks)";
            if (effect.maxStacks > 0)
                stackLabel += $" (max {effect.maxStacks})";

            string durationLabel = string.Empty;
            if (isInstant)
                durationLabel = "instantly";
            else if (isPermanent)
                durationLabel = "permanently";
            else if (duration > 0)
                durationLabel = $"for {duration} turn(s)";

            string durationSuffix = string.IsNullOrEmpty(durationLabel) ? string.Empty : $" {durationLabel}";
            result.AddLog($"{action} status '{statusSkillId}' ({stackLabel}){durationSuffix} ({probability:0.##}% chance).");
        }
        private static List<string> GatherStatusSkillIds(EffectDefinition effect, IReadOnlyList<string> explicitSkillIds)
        {
            var ids = new List<string>();

            if (explicitSkillIds != null)
            {
                foreach (var id in explicitSkillIds)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                        ids.Add(id);
                }
            }

            if (ids.Count == 0 && effect.statusModifySkillIDs != null)
            {
                foreach (var id in effect.statusModifySkillIDs)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                        ids.Add(id);
                }
            }

            if (ids.Count == 0 && !string.IsNullOrWhiteSpace(effect.statusSkillID))
            {
                ids.Add(effect.statusSkillID);
            }

            return ids;
        }

        private static void ApplyModifyStatus(EffectDefinition effect, EffectContext context, EffectInterpretationResult result, HashSet<string> visited)
        {
            float probability = ResolveProbabilityValue(effect, context, context.PrimaryTarget ?? context.Caster);
            var preview = new StatusModificationPreview
            {
                ModifyType = effect.statusModifyType,
                Target = effect.target,
                ReplacementSkillID = effect.statusModifyReplacementSkillID,
                ShowStacks = effect.statusModifyShowStacks,
                StackCount = effect.statusModifyStacks,
                MaxStacks = effect.statusModifyMaxStacks,
                Probability = probability,
                Condition = effect.condition
            };

            if (effect.statusModifySkillIDs != null && effect.statusModifySkillIDs.Count > 0)
            {
                foreach (var id in effect.statusModifySkillIDs)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                        preview.SkillIDs.Add(id);
                }
            }
            if (preview.SkillIDs.Count == 0 && !string.IsNullOrWhiteSpace(effect.statusSkillID))
            {
                preview.SkillIDs.Add(effect.statusSkillID);
            }

            result.StatusModifications.Add(preview);

            string skillsLabel = preview.SkillIDs.Count > 0 ? string.Join(", ", preview.SkillIDs) : "(auto)";
            string stackInfo = string.Empty;
            if (effect.statusModifyShowStacks)
            {
                string maxLabel = effect.statusModifyMaxStacks < 0 ? "inf" : effect.statusModifyMaxStacks.ToString(CultureInfo.InvariantCulture);
                stackInfo = $" (stacks {effect.statusModifyStacks}, max {maxLabel})";
            }

            switch (effect.statusModifyType)
            {
                case StatusModifyType.ApplyStatus:
                    InterpretApplyStatus(effect, context, result, visited, preview.SkillIDs);
                    result.AddLog($"Modify status: apply {skillsLabel}{stackInfo} to {effect.target}.");
                    return;
                case StatusModifyType.ReplaceStatus:
                    result.AddLog($"Modify status: replace {skillsLabel} with {effect.statusModifyReplacementSkillID} on {effect.target}{stackInfo}.");
                    break;
                case StatusModifyType.DeleteStatus:
                    result.AddLog($"Modify status: remove {skillsLabel} from {effect.target}.");
                    break;
                default:
                    result.AddLog($"Modify status operation not specified for {skillsLabel}.");
                    break;
            }
        }

        private static void ApplyConditional(EffectDefinition effect, EffectContext context, EffectInterpretationResult result, HashSet<string> visited)
        {
            float current = context.GetResourceAmount(effect.resourceType);
            bool success = EvaluateComparison(current, effect.compareValue, effect.compareOp);
            result.Conditionals.Add(new ConditionalPreview
            {
                Resource = effect.resourceType,
                Comparison = effect.compareOp,
                CompareValue = effect.compareValue,
                CurrentValue = current,
                Succeeded = success
            });
            result.AddLog($"Conditional {effect.resourceType} {effect.compareOp} {effect.compareValue}: {(success ? "success" : "failed")}");

            if (!success)
                return;

            if (effect.onSuccess == null)
                return;

            foreach (var nested in effect.onSuccess)
            {
                InterpretEffect(nested, context, result, visited);
            }
        }

        private static void ApplyModifySkill(EffectDefinition effect, EffectContext context, EffectInterpretationResult result)
        {
            string targetSkill = GetSkillIdOrSelf(effect, context);
            float probability = ResolveProbabilityValue(effect, context, context.Caster);

            string valueExpression = ResolveValueExpression(effect, context);
            bool isCooldownReset = effect.skillModifyType == SkillModifyType.CooldownReset;
            bool usesValue = effect.skillModifyType != SkillModifyType.CooldownReset &&
                             effect.skillModifyType != SkillModifyType.ForbidUse;

            float resolvedValue = 0f;
            if (usesValue)
                resolvedValue = EvaluateExpression(valueExpression, context, context.Caster, effect.value);

            bool limitEnabled = effect.modifyLimitEnabled && !isCooldownReset;
            float resolvedLimit = 0f;
            if (limitEnabled)
                resolvedLimit = EvaluateExpression(effect.modifyLimitExpression, context, context.Caster, effect.modifyLimitValue);


            var preview = new SkillModificationPreview
            {
                TargetSkillID = targetSkill,
                ModifyType = effect.skillModifyType,
                Operation = effect.skillModifyOperation,
                ModifierType = effect.modifierType,
                ValueExpression = valueExpression,
                AffectAllCosts = effect.modifyAffectsAllCosts,
                CostResource = effect.modifyCostResource,
                Probability = probability,
                Condition = effect.condition,
                LimitEnabled = limitEnabled,
                LimitExpression = limitEnabled ? effect.modifyLimitExpression : string.Empty,
                LimitValue = limitEnabled ? resolvedLimit : 0f
            };

            if (isCooldownReset)
            {
                preview.ValueExpression = effect.resetCooldownToMax ? "Reset" : "Clear";
            }

            result.SkillModifications.Add(preview);

            string log = BuildModifySkillLog(effect, preview, resolvedValue, limitEnabled ? resolvedLimit : 0f);
            result.AddLog(log);
        }

        private static string BuildModifySkillLog(EffectDefinition effect, SkillModificationPreview preview, float resolvedValue, float resolvedLimit)
        {
            string skillLabel = string.IsNullOrWhiteSpace(preview.TargetSkillID) ? "current skill" : $"'{preview.TargetSkillID}'";
            string probabilitySuffix = preview.Probability > 0f ? $" ({preview.Probability:0.##}% chance)" : string.Empty;
            string limitSuffix = preview.LimitEnabled ? $" Limit: {FormatLimitLabel(preview, resolvedLimit)}." : string.Empty;

            switch (preview.ModifyType)
            {
                case SkillModifyType.CooldownReset:
                    {
                        string action = effect.resetCooldownToMax
                            ? $"Reset cooldown of {skillLabel} and start a new cooldown"
                            : $"Clear remaining cooldown of {skillLabel}";
                        return $"{action}{probabilitySuffix}.";
                    }
                case SkillModifyType.ForbidUse:
                    {
                        string message = $"Forbid use of {skillLabel}{probabilitySuffix}.";
                        return preview.LimitEnabled ? message + limitSuffix : message;
                    }
                case SkillModifyType.AddCost:
                    {
                        string resourceLabel = effect.modifyCostResource.ToString();
                        string valueLabel = FormatExpressionLabel(preview.ValueExpression, resolvedValue, ModifierType.Flat);
                        string message = $"Add {resourceLabel} cost {valueLabel} to {skillLabel}{probabilitySuffix}.";
                        return preview.LimitEnabled ? message + limitSuffix : message;
                    }
                case SkillModifyType.ResourceCost:
                    {
                        string costTarget = effect.modifyAffectsAllCosts ? "all costs" : $"{effect.modifyCostResource} cost";
                        var (verb, connector) = GetSkillModifyOperationWords(preview.Operation);
                        string valueLabel = FormatExpressionLabel(preview.ValueExpression, resolvedValue, preview.ModifierType);
                        string modifierSuffix = FormatModifierSuffix(preview.ModifierType);
                        string message = $"{verb} {costTarget} for {skillLabel} {connector} {valueLabel}{modifierSuffix}{probabilitySuffix}.";
                        return preview.LimitEnabled ? message + limitSuffix : message;
                    }
                default:
                    {
                        string category = GetSkillModifyCategory(preview.ModifyType);
                        var (verb, connector) = GetSkillModifyOperationWords(preview.Operation);
                        string valueLabel = FormatExpressionLabel(preview.ValueExpression, resolvedValue, preview.ModifierType);
                        string modifierSuffix = FormatModifierSuffix(preview.ModifierType);
                        string message = $"{verb} {category} of {skillLabel} {connector} {valueLabel}{modifierSuffix}{probabilitySuffix}.";
                        return preview.LimitEnabled ? message + limitSuffix : message;
                    }
            }
            return string.Empty;
        }

        private static string GetSkillModifyCategory(SkillModifyType type)
        {
            switch (type)
            {
                case SkillModifyType.Range:
                    return "range";
                case SkillModifyType.CooldownModify:
                    return "cooldown";
                case SkillModifyType.TimeCost:
                    return "time cost";
                case SkillModifyType.Damage:
                    return "damage";
                case SkillModifyType.Heal:
                    return "healing";
                case SkillModifyType.Duration:
                    return "duration";
                case SkillModifyType.BuffPower:
                    return "buff power";
                default:
                    return "value";
            }
        }

        private static (string verb, string connector) GetSkillModifyOperationWords(SkillModifyOperation operation)
        {
            switch (operation)
            {
                case SkillModifyOperation.Override:
                    return ("Set", "to");
                case SkillModifyOperation.Multiply:
                    return ("Scale", "by");
                default:
                    return ("Reduce", "by");
            }
        }

        private static string FormatExpressionLabel(string expression, float resolvedValue, ModifierType modifierType)
        {
            string suffix = modifierType == ModifierType.Percentage ? "%" : string.Empty;
            string resolvedText = resolvedValue.ToString("0.###", CultureInfo.InvariantCulture) + suffix;

            if (string.IsNullOrWhiteSpace(expression))
                return resolvedText;

            if (float.TryParse(expression, NumberStyles.Float, CultureInfo.InvariantCulture, out float numeric))
            {
                string numericText = numeric.ToString("0.###", CultureInfo.InvariantCulture);
                return modifierType == ModifierType.Percentage ? numericText + "%" : numericText;
            }

            return $"'{expression}' (~{resolvedText})";
        }

        private static string FormatModifierSuffix(ModifierType modifierType)
        {
            switch (modifierType)
            {
                case ModifierType.Percentage:
                    return " (Percentage)";
                case ModifierType.Flat:
                    return " (Flat)";
                default:
                    return string.Empty;
            }
        }

        private static string FormatLimitLabel(SkillModificationPreview preview, float resolvedLimit)
        {
            string resolvedText = resolvedLimit.ToString("0.###", CultureInfo.InvariantCulture);

            if (string.IsNullOrWhiteSpace(preview.LimitExpression))
                return resolvedText;

            if (float.TryParse(preview.LimitExpression, NumberStyles.Float, CultureInfo.InvariantCulture, out float numeric))
                return numeric.ToString("0.###", CultureInfo.InvariantCulture);

            return $"'{preview.LimitExpression}' (~{resolvedText})";
        }

        private static void ApplyReplaceSkill(EffectDefinition effect, EffectContext context, EffectInterpretationResult result)
        {
            string targetSkill = GetSkillIdOrSelf(effect, context);
            float probability = ResolveProbabilityValue(effect, context, context.Caster);

            result.SkillReplacements.Add(new SkillReplacementPreview
            {
                TargetSkillID = targetSkill,
                NewSkillID = effect.replaceSkillID,
                InheritCooldown = effect.inheritReplacedCooldown,
                Probability = probability,
                Condition = effect.condition
            });

            result.AddLog($"Replace skill '{targetSkill}' with '{effect.replaceSkillID}' (inherit cooldown: {effect.inheritReplacedCooldown}).");
        }

        private static void ApplyMove(EffectDefinition effect, EffectContext context, EffectInterpretationResult result)
        {
            float probability = ResolveProbabilityValue(effect, context, context.PrimaryTarget ?? context.Caster);
            var preview = new MovePreview
            {
                Subject = effect.moveSubject,
                Execution = effect.moveExecution,
                Direction = effect.moveDirection,
                Distance = effect.moveDistance,
                MaxDistance = effect.moveMaxDistance,
                Offset = effect.moveOffset,
                ForceMovement = effect.forceMovement,
                AllowPartialMove = effect.allowPartialMove,
                IgnoreObstacles = effect.moveIgnoreObstacles,
                StopAdjacentToTarget = effect.moveStopAdjacentToTarget,
                Target = effect.target,
                Probability = probability,
                Condition = effect.condition
            };

            result.Moves.Add(preview);
            result.AddLog($"Move {effect.moveSubject} via {effect.moveExecution} ({effect.moveDirection}).");
        }

        private static void ApplyModifyAction(EffectDefinition effect, EffectContext context, EffectInterpretationResult result)
        {
            string targetSkill = GetSkillIdOrSelf(effect, context);
            float probability = ResolveProbabilityValue(effect, context, context.Caster);
            var preview = new ActionModificationPreview
            {
                TargetSkillID = targetSkill,
                ActionFilter = effect.targetActionType,
                ModifyType = effect.actionModifyType,
                ModifierType = effect.modifierType,
                ValueExpression = ResolveValueExpression(effect, context),
                ActionTypeOverride = effect.actionTypeOverride,
                Probability = probability,
                Condition = effect.condition
            };
            result.ActionModifications.Add(preview);
            result.AddLog($"Modify actions on '{targetSkill}' ({effect.actionModifyType}).");
        }

        private static void ApplyModifyDamageSchool(EffectDefinition effect, EffectContext context, EffectInterpretationResult result)
        {
            string targetSkill = GetSkillIdOrSelf(effect, context);
            float probability = ResolveProbabilityValue(effect, context, context.Caster);
            DamageSchoolModifyType modifyType = effect.damageSchoolModifyType;
            bool useFilter = effect.damageSchoolFilterEnabled;
            string filterLabel = useFilter ? effect.damageSchoolFilter.ToString() : string.Empty;
            string expression = modifyType == DamageSchoolModifyType.Damage
                ? ResolveValueExpression(effect, context)
                : string.Empty;
            var preview = new DamageSchoolModificationPreview
            {
                TargetSkillID = targetSkill,
                ModifyType = modifyType,
                TargetSchool = effect.damageSchool,
                Operation = effect.skillModifyOperation,
                ModifierType = effect.modifierType,
                ValueExpression = expression,
                UseFilter = useFilter,
                Filter = effect.damageSchoolFilter,
                Probability = probability,
                Condition = effect.condition
            };

            result.DamageSchoolModifications.Add(preview);
            string filterSuffix = useFilter ? $" (filter: {filterLabel})" : string.Empty;
            switch (modifyType)
            {
                case DamageSchoolModifyType.Damage:
                    {
                        string valueSuffix = string.IsNullOrWhiteSpace(expression) ? string.Empty : $" by '{expression}'";
                        result.AddLog($"Modify {effect.damageSchool} damage on '{targetSkill}'{filterSuffix} ({effect.skillModifyOperation}, {effect.modifierType}){valueSuffix}.");
                        break;
                    }
                case DamageSchoolModifyType.DamageSchoolType:
                    result.AddLog($"Convert damage on '{targetSkill}'{filterSuffix} to {effect.damageSchool}.");
                    break;
            }
        }

        private static void ApplyAttributeModifier(EffectDefinition effect, EffectContext context, EffectInterpretationResult result)
        {
            float probability = ResolveProbabilityValue(effect, context, context.Caster);
            int duration = ResolveDuration(effect, context);
            int stacks = ResolveStackCount(effect, context);
            string expression = ResolveValueExpression(effect, context);

            var preview = new AttributeModifierPreview
            {
                Attribute = effect.attributeType,
                ModifierType = effect.modifierType,
                ValueExpression = expression,
                Duration = duration,
                StackCount = stacks,
                Target = effect.target,
                Probability = probability,
                Condition = effect.condition,
                ImmunityScope = effect.immunityScope
            };

            result.AttributeModifiers.Add(preview);

            if (effect.attributeType == AttributeType.Immune)
                result.AddLog($"Attribute modifier {effect.attributeType} ({effect.immunityScope}).");
            else
                result.AddLog($"Attribute modifier {effect.attributeType} ({expression}).");
        }

        private static void ApplyMasteryPosture(EffectDefinition effect, EffectContext context, EffectInterpretationResult result)
        {
            float probability = ResolveProbabilityValue(effect, context, context.Caster);
            var settings = effect.masteryPosture ?? new MasteryPostureSettings();

            result.MasteryPosture.Add(new MasteryPosturePreview
            {
                LockArmorToZero = settings.lockArmorToZero,
                ArmorToHpRatio = settings.armorToHpRatio,
                ArmorToEnergyRatio = settings.armorToEnergyRatio,
                PostureResource = settings.postureResource,
                PostureMaxHealthRatio = settings.postureMaxHealthRatio,
                PostureMaxExpression = settings.postureMaxExpression,
                MasteryScalingExpression = settings.masteryScalingExpression,
                DamageConversionExpression = ResolveValueExpression(effect, context),
                RecoveryPercentPerTurn = settings.postureRecoveryPercentPerTurn,
                BreakDamageMultiplier = settings.postureBreakExtraDamageMultiplier,
                BreakStatusSkillID = settings.postureBreakStatusSkillID,
                BreakDuration = settings.postureBreakDurationTurns,
                BreakSkipsTurn = settings.postureBreakSkipsTurn,
                Probability = probability,
                Condition = effect.condition
            });

            result.AddLog("Mastery posture engine configured.");
        }

        private static void ApplyCooldownModifier(EffectDefinition effect, EffectContext context, EffectInterpretationResult result)
        {
            int seconds = effect.cooldownChangeSeconds;
            int turns = 0;
            if (seconds != 0)
            {
                int baseTurnSeconds = CombatClock.BaseTurnSeconds; // 每回合基础秒数（如6秒）
                if (seconds > 0)
                {
                    // 冷却增加：不足1回合也向上取整
                    int abs = Mathf.CeilToInt((float)seconds / baseTurnSeconds);
                    turns = Mathf.Max(abs, 1); //至少增加1回合
                }
                else
                {
                    // 冷却减少：仅当减少的秒数 ≥ 1回合时才生效，否则不减少
                    int reduceSeconds = Mathf.Abs(seconds); // 取减少的秒数绝对值
                    if (reduceSeconds >= baseTurnSeconds)
                    {
                        // ٵ㹻1غϣʵʻغ㣨ȡ
                        // 减少的秒数足够1回合，按实际回合数计算（向下取整，避免多减）
                        turns = -Mathf.FloorToInt((float)reduceSeconds / baseTurnSeconds);
                    }
                    // reduceSeconds < baseTurnSecondsturns0٣
                    // 否则（reduceSeconds < baseTurnSeconds），turns保持0（不减少）
                }
            }

            float probability = ResolveProbabilityValue(effect, context, context.Caster);
            string selfSkillId = context.Skill != null ? context.Skill.skillID : string.Empty;
            var scope = effect.cooldownTargetScope;
            result.CooldownModifications.Add(new CooldownModificationPreview
            {
                Scope = scope,
                SelfSkillID = selfSkillId,
                Seconds = seconds,
                turns = turns,
                Probability = probability,
                Condition = effect.condition
            });

            string scopeDescription = scope switch
            {
                CooldownTargetScope.All => "all skills",
                CooldownTargetScope.ExceptRed => "all non-ultimate (non-Red) skills",
                _ => !string.IsNullOrWhiteSpace(selfSkillId) ? $"skill '{selfSkillId}'" : "own skill"
            };

            result.AddLog($"Modify cooldown for {scopeDescription} by {seconds:+#;-#;0}s ({turns:+#;-#;0} turns).");
        }
        private static void ApplyRandomOutcome(EffectDefinition effect, EffectContext context, EffectInterpretationResult result, HashSet<string> visited)
        {
            if (effect.randomOutcomes == null || effect.randomOutcomes.Count == 0)
            {
                result.AddLog("RandomOutcome effect has no options configured.");
                return;
            }

            int rollCount = Mathf.Max(1, effect.randomRollCount);
            bool allowDuplicates = effect.randomAllowDuplicates;

            int weightedSum = 0;
            int configuredOptions = 0;
            foreach (var entry in effect.randomOutcomes)
            {
                if (entry == null)
                    continue;
                configuredOptions++;
                weightedSum += Mathf.Max(0, entry.weight);
            }

            bool useUniformWeights = weightedSum <= 0;
            if (useUniformWeights)
                weightedSum = Mathf.Max(1, configuredOptions);
            else if (weightedSum <= 0)
                weightedSum = 1;

            var preview = new RandomOutcomePreview
            {
                RollCount = rollCount,
                AllowDuplicates = allowDuplicates,
                Condition = effect.condition
            };

            int optionIndex = 0;
            foreach (var entry in effect.randomOutcomes)
            {
                if (entry == null)
                    continue;

                optionIndex++;
                int weight = Mathf.Max(0, entry.weight);
                if (useUniformWeights || weight == 0)
                    weight = 1;
                float probability = weightedSum > 0 ? (weight / (float)weightedSum) * 100f : 0f;
                string label = !string.IsNullOrWhiteSpace(entry.label) ? entry.label : $"Option {optionIndex}";

                var optionPreview = new RandomOutcomeOptionPreview
                {
                    Label = label,
                    Description = entry.description,
                    Probability = probability,
                    Weight = weight,
                    ProbabilityMode = entry.probabilityMode
                };

                if (entry.effects != null && entry.effects.Count > 0)
                {
                    var nested = new EffectInterpretationResult();
                    foreach (var nestedEffect in entry.effects)
                        InterpretEffect(nestedEffect, context, nested, visited);
                    optionPreview.Result = nested;
                }

                preview.Options.Add(optionPreview);
            }

            result.RandomOutcomes.Add(preview);
            result.AddLog($"Random outcome: {preview.Options.Count} option(s), roll {rollCount} time(s).");
        }

        private static void ApplyRepeat(EffectDefinition effect, EffectContext context, EffectInterpretationResult result, HashSet<string> visited)
        {
            string expression = GetRepeatCountExpression(effect, context);
            int count = EvaluateRepeatCount(effect, context, expression);
            if (effect.repeatMaxCount > 0)
                count = Mathf.Min(count, effect.repeatMaxCount);
            count = Mathf.Max(0, count);

            var preview = new RepeatEffectPreview
            {
                CountSource = effect.repeatCountSource,
                Count = count,
                MaxCount = effect.repeatMaxCount,
                CountExpression = expression,
                ResourceType = effect.repeatResourceType,
                ConsumeResource = effect.repeatConsumeResource,
                Condition = effect.condition
            };

            if (effect.repeatEffects != null && effect.repeatEffects.Count > 0 && count > 0)
            {
                var aggregate = new EffectInterpretationResult();
                for (int i = 0; i < count; i++)
                {
                    foreach (var nested in effect.repeatEffects)
                        InterpretEffect(nested, context, aggregate, visited);
                }

                preview.Result = aggregate;
                result.Append(aggregate);
            }
            else if (effect.repeatEffects == null || effect.repeatEffects.Count == 0)
            {
                result.AddLog("Repeat effect has no nested effects configured.");
            }

            result.RepeatEffects.Add(preview);

            string sourceDescription = effect.repeatCountSource switch
            {
                RepeatCountSource.ResourceValue => $"based on {effect.repeatResourceType} value",
                RepeatCountSource.ResourceSpent => $"based on {effect.repeatResourceType} spent",
                RepeatCountSource.Expression => string.IsNullOrWhiteSpace(expression) ? "expression" : $"expression '{expression}'",
                _ => $"fixed {Mathf.Max(0, effect.repeatCount)}"
            };

            result.AddLog($"Repeat nested effects {count} time(s) ({sourceDescription}).");
        }

        private static void ApplyProbabilityModifier(EffectDefinition effect, EffectContext context, EffectInterpretationResult result)
        {
            var preview = new ProbabilityModifierPreview
            {
                Mode = effect.probabilityModifierMode,
                Condition = effect.condition,
                Target = effect.target
            };

            result.ProbabilityModifiers.Add(preview);
            result.AddLog($"Probability modifier: {effect.probabilityModifierMode}.");
        }

        private static void ApplyDotHotModifier(EffectDefinition effect, EffectContext context, EffectInterpretationResult result, HashSet<string> visited)
        {
            string expression = ResolveValueExpression(effect, context);
            float evaluatedValue = 0f;
            bool hasExpression = !string.IsNullOrWhiteSpace(expression);
            var referenceUnit = context.PrimaryTarget ?? context.Caster;
            if (hasExpression)
                evaluatedValue = EvaluateExpression(expression, context, referenceUnit, effect.value);

            int baseTriggerCount = Mathf.Max(0, effect.dotHotBaseTriggerCount);
            int duration = ResolveDuration(effect, context);
            float probability = ResolveProbabilityValue(effect, context, referenceUnit);

            var preview = new DotHotModifierPreview
            {
                Operation = effect.dotHotOperation,
                BaseTriggerCount = baseTriggerCount,
                ValueExpression = expression,
                EvaluatedValue = evaluatedValue,
                Duration = duration,
                DamageSchool = effect.damageSchool,
                CanCrit = effect.canCrit,
                Target = effect.target,
                Probability = probability,
                Condition = effect.condition,
                SupportsStacks = effect.dotHotShowStacks,
                MaxStacks = effect.dotHotShowStacks ? effect.dotHotMaxStacks : 1
            };

            if (effect.dotHotAdditionalEffects != null && effect.dotHotAdditionalEffects.Count > 0)
            {
                var additional = new EffectInterpretationResult();
                foreach (var nested in effect.dotHotAdditionalEffects)
                    InterpretEffect(nested, context, additional, visited);
                preview.AdditionalEffects = additional;
                result.Append(additional);
            }

            result.DotHotModifiers.Add(preview);

            string durationLabel = duration switch
            {
                > 0 => $"{duration} round(s)",
                -1 => "instant",
                -2 => "permanent",
                0 => "skill default",
                _ => duration.ToString()
            };

            string triggerLabel = baseTriggerCount == 0 ? "default (6s)" : baseTriggerCount.ToString();
            string triggerFormulaLabel = baseTriggerCount == 0 ? "default6" : baseTriggerCount.ToString();
            string expressionLabel = string.IsNullOrWhiteSpace(expression) ? "(no tick expression)" : expression;
            string valueLabel = hasExpression ? evaluatedValue.ToString("0.##") : "--";
            string stackLabel = effect.dotHotShowStacks ? (effect.dotHotMaxStacks < 0 ? "stacks: unlimited" : $"stacks: max {effect.dotHotMaxStacks}") : "stacks: disabled";
            switch (effect.dotHotOperation)
            {
                case DotHotOperation.None:
                    result.AddLog($"DoT/HoT modifier (no direct damage) targeting {effect.target} | base trigger {triggerLabel}, duration {durationLabel}, probability {probability:0.##}% ({stackLabel}).");
                    break;
                case DotHotOperation.TriggerDots:
                    result.AddLog($"Trigger DoT ticks for {effect.target}: base trigger {triggerLabel}, duration {durationLabel}, probability {probability:0.##}%, tick value {valueLabel} (expr: {expressionLabel}).  ({stackLabel}).Formula: ((6s+速度时间)/{triggerFormulaLabel})*(持续回合/2)*数值.");
                    break;
                case DotHotOperation.TriggerHots:
                    result.AddLog($"Trigger HoT ticks for {effect.target}: base trigger {triggerLabel}, duration {durationLabel}, probability {probability:0.##}%, tick value {valueLabel} (expr: {expressionLabel}).  ({stackLabel}).Formula: ((6s+速度时间)/{triggerFormulaLabel})*(持续回合/2)*数值.");
                    break;
                case DotHotOperation.ConvertDamageToDot:
                    result.AddLog($"Convert damage into DoT for {effect.target}: base trigger {triggerLabel}, duration {durationLabel}, probability {probability:0.##}%, tick value {valueLabel} (expr: {expressionLabel}).  ({stackLabel}).Formula: ((6s+速度时间)/{triggerFormulaLabel})*(持续回合/2)*数值.");
                    break;
            }
        }

        private static string GetRepeatCountExpression(EffectDefinition effect, EffectContext context)
        {
            if (effect.repeatCountSource != RepeatCountSource.Expression)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(effect.repeatCountExpression))
                return effect.repeatCountExpression;
            return ResolveValueExpression(effect, context);
        }

        private static int EvaluateRepeatCount(EffectDefinition effect, EffectContext context, string expression)
        {
            int fallback = Mathf.Max(0, effect.repeatCount);
            switch (effect.repeatCountSource)
            {
                case RepeatCountSource.Fixed:
                    return fallback;
                case RepeatCountSource.Expression:
                    if (string.IsNullOrWhiteSpace(expression))
                        return fallback;
                    float exprValue = EvaluateExpression(expression, context, context.PrimaryTarget ?? context.Caster, fallback);
                    return Mathf.Max(0, Mathf.FloorToInt(exprValue));
                case RepeatCountSource.ResourceValue:
                    return Mathf.Max(0, Mathf.FloorToInt(context.GetResourceAmount(effect.repeatResourceType)));
                case RepeatCountSource.ResourceSpent:
                    float spent = context.GetResourceSpent(effect.repeatResourceType);
                    if (spent <= 0f && context.LastResourceSpendType == effect.repeatResourceType)
                        spent = context.LastResourceSpendAmount;
                    return Mathf.Max(0, Mathf.FloorToInt(spent));
                default:
                    return fallback;
            }
        }
        private static bool CheckCondition(EffectDefinition effect, EffectContext context)
        {
            switch (effect.condition)
            {
                case EffectCondition.None:
                    return true;
                case EffectCondition.AfterAttack:
                    return context.ConditionAfterAttack && MatchesConditionTarget(effect.conditionTarget, context);
                case EffectCondition.OnPerformAttack:
                    return context.ConditionOnPerformAttack && MatchesConditionTarget(effect.conditionTarget, context);
                case EffectCondition.OnPerformHeal:
                    return context.ConditionOnPerformHeal && MatchesConditionTarget(effect.conditionTarget, context);
                case EffectCondition.OnCriticalHit:
                    return context.ConditionOnCrit;
                case EffectCondition.OnCooldownEnd:
                    return context.ConditionOnCooldownEnd;
                case EffectCondition.AfterSkillUse:
                    if (!context.ConditionAfterSkillUse)
                        return false;
                    string requiredSkill = effect.conditionSkillUseID;
                    if (string.IsNullOrWhiteSpace(requiredSkill) || string.Equals(requiredSkill, "any", StringComparison.OrdinalIgnoreCase))
                        return true;
                    string lastSkill = context.LastSkillUsedID;
                    return !string.IsNullOrWhiteSpace(lastSkill) &&
                           string.Equals(requiredSkill, lastSkill, StringComparison.OrdinalIgnoreCase);
                case EffectCondition.SkillStateActive:
                    if (!context.ConditionSkillStateActive)
                        return false;

                    string requiredState = effect.conditionSkillStateID;
                    if (string.IsNullOrWhiteSpace(requiredState))
                        return true;

                    int stateStacks = context.GetSkillStateStacks(requiredState);
                    if (stateStacks <= 0)
                        return false;

                    if (effect.conditionSkillStateCheckStacks)
                        return EvaluateComparison(stateStacks, effect.conditionSkillStateStacks, effect.conditionSkillStateStackCompare);

                    return true;
                case EffectCondition.OnDotHotActive:
                    context.ConditionDotStacks = 0f;
                    if (context.ActiveDotHotStatuses == null || context.ActiveDotHotStatuses.Count == 0)
                        return false;

                    TargetType dotTarget = effect.conditionDotTarget;
                    var skillList = effect.conditionDotSkillIDs;
                    bool matchAnySkill = skillList == null || skillList.Count == 0;
                    int matchedEntries = 0;
                    int totalStacks = 0;

                    foreach (var status in context.ActiveDotHotStatuses)
                    {
                        if (status == null)
                            continue;

                        if (dotTarget != TargetType.All && status.Target != dotTarget)
                            continue;

                        bool skillMatch = matchAnySkill;
                        if (!skillMatch && !string.IsNullOrWhiteSpace(status.SkillID))
                        {
                            foreach (var id in skillList)
                            {
                                if (!string.IsNullOrWhiteSpace(id) && string.Equals(id, status.SkillID, StringComparison.OrdinalIgnoreCase))
                                {
                                    skillMatch = true;
                                    break;
                                }
                            }
                        }

                        if (!skillMatch)
                            continue;

                        matchedEntries++;
                        int stacks = Mathf.Max(0, status.Stacks);
                        if (effect.conditionDotUseStacks)
                            totalStacks += stacks <= 0 ? 1 : stacks;
                    }

                    if (matchedEntries == 0)
                        return false;

                    context.ConditionDotStacks = effect.conditionDotUseStacks ? Mathf.Max(0, totalStacks) : matchedEntries;
                    return true;
                case EffectCondition.OnNextSkillSpendResource:
                    if (!context.ConditionOnResourceSpend)
                        return false;
                    float spent = context.GetResourceSpent(effect.conditionResourceType);
                    if (spent < effect.conditionMinAmount)
                        return false;
                    context.LastResourceSpendType = effect.conditionResourceType;
                    context.LastResourceSpendAmount = spent;
                    return true;
                case EffectCondition.OnEffectEnd:
                    return context.ConditionOnEffectEnd;
                case EffectCondition.OnDamageTaken:
                    return context.ConditionOnDamageTaken;
                default:
                    return true;
            }
        }

        private static List<Unit> ResolveTargets(TargetType targetType, EffectContext context)
        {
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
                    else if (context.Enemies.Count > 0)
                        targets.AddRange(context.Enemies);
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

        private static string ResolveValueExpression(EffectDefinition effect, EffectContext context)
        {
            int level = context.ResolveSkillLevel(context.Skill);
            if (effect.perLevel && effect.valueExprLevels != null && effect.valueExprLevels.Length >= level)
            {
                int idx = Mathf.Clamp(level - 1, 0, effect.valueExprLevels.Length - 1);
                string candidate = effect.valueExprLevels[idx];
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }

            if (!string.IsNullOrWhiteSpace(effect.valueExpression))
                return effect.valueExpression;

            if (Mathf.Abs(effect.value) > float.Epsilon)
                return effect.value.ToString(CultureInfo.InvariantCulture);

            return "0";
        }

        private static float EvaluateExpression(string expression, EffectContext context, Unit target, float fallback)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return fallback;

            var variables = BuildVariableMap(context, target);
            if (Formula.TryEvaluate(expression, variables, out var value))
                return value;

            if (float.TryParse(expression, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return value;

            return fallback;
        }
        private static void PopulateDamagePreview(DamagePreview preview, EffectDefinition effect, EffectContext context, Unit target)
        {
            if (preview == null || context?.Caster?.Stats == null)
                return;

            var stats = context.Caster.Stats;
            float primaryAttribute = ResolvePrimaryOffensiveStat(stats);
            float additionalMultiplier = ResolveCustomVariable(context, "damageincrease1");
            float situationalMultiplier = ResolveCustomVariable(context, "damageincrease2");
            float damageReduction = ResolveCustomVariable(context, "damagereduction");

            string mitigationKey;
            float armorMultiplier = 1f;
            switch (effect.damageSchool)
            {
                case DamageSchool.Poison:
                    mitigationKey = DamageSchool.Physical.ToString().ToLowerInvariant() + "_mitigation";
                    break;
                case DamageSchool.Bleed:
                    mitigationKey = string.Empty;
                    armorMultiplier = 1f;
                    break;
                case DamageSchool.Frost:
                    mitigationKey = "magical_mitigation";
                    break;
                // 新增：火焰伤害使用魔法减免
                case DamageSchool.Fire:
                    mitigationKey = "magical_mitigation";
                    break;
                default:
                    mitigationKey = effect.damageSchool.ToString().ToLowerInvariant() + "_mitigation";
                    break;
            }

            if (!string.IsNullOrEmpty(mitigationKey))
            {
                armorMultiplier = ResolveCustomVariable(context, mitigationKey);
            }

            if (armorMultiplier <= 0f)
                armorMultiplier = 1f;

            float skillThreat = context.Skill != null ? context.Skill.threat : 0f;
            float skillShred = context.Skill != null ? context.Skill.shredMultiplier : 0f;

            var input = new CombatFormula.DamageInput
            {
                SkillDamage = preview.Amount,
                IsCritical = false,
                PrimaryAttributeValue = primaryAttribute,
                AdditionalDamageMultiplier = additionalMultiplier,
                SituationalDamageMultiplier = situationalMultiplier,
                DamageReduction = damageReduction,
                ArmorMitigationMultiplier = armorMultiplier,
                SkillThreatMultiplier = skillThreat,
                SkillShredMultiplier = skillShred
            };

            var normal = CombatFormula.CalculateDamage(input, stats);
            preview.ExpectedNormalDamage = normal.Damage;
            preview.ExpectedThreat = normal.Threat;
            preview.ExpectedShred = normal.Shred;
            preview.AttributeScalingMultiplier = normal.AttributeMultiplier;
            preview.CriticalMultiplier = 1f;

            if (preview.CanCrit)
            {
                input.IsCritical = true;
                var critical = CombatFormula.CalculateDamage(input, stats);
                preview.ExpectedCriticalDamage = critical.Damage;
                preview.ExpectedCriticalThreat = critical.Threat;
                preview.ExpectedCriticalShred = critical.Shred;
                preview.CriticalMultiplier = critical.CritMultiplier;
            }
            else
            {
                preview.ExpectedCriticalDamage = preview.ExpectedNormalDamage;
                preview.ExpectedCriticalThreat = preview.ExpectedThreat;
                preview.ExpectedCriticalShred = preview.ExpectedShred;
            }
        }
        private static float ResolveProbabilityValue(EffectDefinition effect, EffectContext context, Unit target)
        {
            string expr = ResolveProbabilityExpression(effect, context);
            if (string.IsNullOrWhiteSpace(expr))
                return 100f;

            string trimmed = expr.Trim();
            if (trimmed.EndsWith("%"))
                trimmed = trimmed.Substring(0, trimmed.Length - 1);

            var variables = BuildVariableMap(context, target);
            if (Formula.TryEvaluate(trimmed, variables, out var value))
                return value;

            if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return value;

            return 100f;
        }

        private static string ResolveProbabilityExpression(EffectDefinition effect, EffectContext context)
        {
            bool showProbability = (effect.visibleFields & EffectFieldMask.Probability) != 0;
            if (!showProbability)
                return string.Empty;

            int level = context.ResolveSkillLevel(context.Skill);
            if (effect.perLevel && effect.probabilityLvls != null && effect.probabilityLvls.Length >= level)
            {
                int idx = Mathf.Clamp(level - 1, 0, effect.probabilityLvls.Length - 1);
                string candidate = effect.probabilityLvls[idx];
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }

            return effect.probability;
        }
        private static float ResolveCustomVariable(EffectContext context, string key)
        {
            if (context == null || string.IsNullOrWhiteSpace(key))
                return 0f;

            if (context.CustomVariables != null && context.CustomVariables.TryGetValue(key, out float value))
                return value;

            return 0f;
        }

        private static float ResolvePrimaryOffensiveStat(Stats stats)
        {
            if (stats == null)
                return 0f;
            // Skills currently pick the higher of Strength or Agility for preview scaling.
            return Math.Max(stats.Strength, stats.Agility);
        }

        private static int ResolveDuration(EffectDefinition effect, EffectContext context)
        {
            bool useCustom = (effect.visibleFields & EffectFieldMask.Duration) != 0;
            if (!useCustom)
            {
                if (context.Skill != null)
                    return context.Skill.ResolveDuration(context.ResolveSkillLevel(context.Skill));
                return 0;
            }

            int level = context.ResolveSkillLevel(context.Skill);
            if (UsesPerLevelDuration(effect) && effect.durationLevels != null && effect.durationLevels.Length >= level)
            {
                int idx = Mathf.Clamp(level - 1, 0, effect.durationLevels.Length - 1);
                int candidate = effect.durationLevels[idx];
                if (candidate != 0)
                    return candidate;
            }

            return Mathf.RoundToInt(effect.duration);
        }

        private static int ResolveStackCount(EffectDefinition effect, EffectContext context)
        {
            int baseStacks = effect.stackCount > 0 ? effect.stackCount : 1;
            int level = context.ResolveSkillLevel(context.Skill);
            if (effect.perLevel && effect.stackCountLevels != null && effect.stackCountLevels.Length >= level)
            {
                int idx = Mathf.Clamp(level - 1, 0, effect.stackCountLevels.Length - 1);
                int value = effect.stackCountLevels[idx];
                if (value > 0)
                    return value;
            }
            return baseStacks;
        }

        private static Dictionary<string, float> BuildVariableMap(EffectContext context, Unit target)
        {
            var map = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in context.CustomVariables)
                map[kvp.Key] = kvp.Value;

            if (!map.ContainsKey("dotStacks"))
                map["dotStacks"] = context.ConditionDotStacks;


            if (context.Caster != null)
            {
                AddStats(map, context.Caster.Stats, string.Empty);
                map["p"] = context.Caster.Stats?.Mastery ?? 0f;
            }
            else
            {
                map["p"] = 0f;
            }

            if (target != null)
                AddStats(map, target.Stats, "target_");

            map["damage"] = context.IncomingDamage;
            map["damage_pre"] = context.IncomingDamage;
            map["damage_post"] = context.IncomingDamageMitigated;
            map["spent"] = context.LastResourceSpendAmount;
            map["currentdamage"] = context.IncomingDamage;
            map["heal"] = context.IncomingHealing;
            map["currentheal"] = context.IncomingHealing;
            if (context.ResourceValues.TryGetValue(ResourceType.Energy, out var energyValue))
                map["currentenergy"] = energyValue;

            if (context.LastResourceSpendType.HasValue)
            {
                string key = $"spent_{context.LastResourceSpendType.Value.ToString().ToLowerInvariant()}";
                map[key] = context.LastResourceSpendAmount;
            }

            foreach (var kvp in context.ResourceSpent)
            {
                string key = $"spent_{kvp.Key.ToString().ToLowerInvariant()}";
                map[key] = kvp.Value;
            }

            foreach (var kvp in context.ResourceValues)
            {
                string key = kvp.Key.ToString().ToLowerInvariant();
                map[key] = kvp.Value;
            }

            foreach (var kvp in context.ResourceMaxValues)
            {
                string key = kvp.Key.ToString().ToLowerInvariant() + "_max";
                map[key] = kvp.Value;
            }


            map["skillLevel"] = context.ResolveSkillLevel(context.Skill);
            return map;
        }

        private static void AddStats(IDictionary<string, float> map, Stats stats, string prefix)
        {
            if (stats == null) return;
            map[prefix + "atk"] = stats.Attack;
            map[prefix + "attack"] = stats.Attack;
            map[prefix + "hp"] = stats.HP;
            map[prefix + "maxhp"] = stats.MaxHP;
            map[prefix + "armor"] = stats.Armor;
            map[prefix + "crit"] = stats.Crit;
            map[prefix + "critdamage"] = stats.CritDamage;
            map[prefix + "speed"] = stats.Speed;
            map[prefix + "movespeed"] = stats.MoveSpeed;
            map[prefix + "stamina"] = stats.Stamina;
            map[prefix + "energy"] = stats.Energy;
            map[prefix + "maxenergy"] = stats.MaxEnergy;
            map[prefix + "energyregen"] = stats.EnergyRegenPer2s;
            map[prefix + "mastery"] = stats.Mastery;
            map[prefix + "posture"] = stats.Posture;
            map[prefix + "maxposture"] = stats.MaxPosture;
            map[prefix + "strength"] = stats.Strength;
            map[prefix + "str"] = stats.Strength;
            map[prefix + "agility"] = stats.Agility;
            map[prefix + "agi"] = stats.Agility;
            map[prefix + "damageincrease"] = stats.DamageIncrease;
            map[prefix + "healincrease"] = stats.HealIncrease;
            map[prefix + "dmginc"] = stats.DamageIncrease;
            map[prefix + "threat"] = stats.Threat;
            map[prefix + "shred"] = stats.Shred;
        }

        private static bool IsMaxExpression(string expression)
        {
            return !string.IsNullOrWhiteSpace(expression) &&
                   string.Equals(expression.Trim(), "max", StringComparison.OrdinalIgnoreCase);
        }

        private static float CalculateMaxFillAmount(ResourceType resourceType, EffectContext context, Unit target)
        {
            float current = GetResourceValue(resourceType, context, target);
            float max = GetResourceMaxValue(resourceType, context, target);
            if (max <= 0f)
                return 0f;
            return Mathf.Max(0f, max - current);
        }

        private static float GetResourceValue(ResourceType resourceType, EffectContext context, Unit target)
        {
            if (target?.Stats != null)
            {
                switch (resourceType)
                {
                    case ResourceType.HP:
                        return target.Stats.HP;
                    case ResourceType.Energy:
                        return target.Stats.Energy;
                    case ResourceType.posture:
                        return target.Stats.Posture;
                }
            }

            return context.GetResourceAmount(resourceType);
        }

        private static float GetResourceMaxValue(ResourceType resourceType, EffectContext context, Unit target)
        {
            if (target?.Stats != null)
            {
                switch (resourceType)
                {
                    case ResourceType.HP:
                        return target.Stats.MaxHP;
                    case ResourceType.Energy:
                        return target.Stats.MaxEnergy;
                    case ResourceType.posture:
                        return target.Stats.MaxPosture;
                }
            }

            return context.GetResourceMax(resourceType);
        }


        private static string GetSkillIdOrSelf(EffectDefinition effect, EffectContext context)
        {
            if (!string.IsNullOrWhiteSpace(effect.targetSkillID))
                return effect.targetSkillID;
            if (context.Skill != null && !string.IsNullOrWhiteSpace(context.Skill.skillID))
                return context.Skill.skillID;
            return string.Empty;
        }

        private static string DescribeUnit(Unit unit)
        {
            if (unit == null)
                return "target";
            if (!string.IsNullOrWhiteSpace(unit.UnitId))
                return unit.UnitId;
            return "target";
        }
        private static bool UsesPerLevelDuration(EffectDefinition effect)
        {
            if (effect == null)
                return false;

            if (effect.perLevelDuration)
                return true;

            if (!effect.perLevel || effect.durationLevels == null)
                return false;

            for (int i = 0; i < effect.durationLevels.Length; i++)
            {
                if (effect.durationLevels[i] != 0)
                    return true;
            }

            return false;
        }
        private static bool EvaluateComparison(float current, float compareValue, CompareOp op)
        {
            switch (op)
            {
                case CompareOp.Equal:
                    return Mathf.Approximately(current, compareValue);
                case CompareOp.Greater:
                    return current > compareValue;
                case CompareOp.GreaterEqual:
                    return current >= compareValue;
                case CompareOp.Less:
                    return current < compareValue;
                case CompareOp.LessEqual:
                    return current <= compareValue;
                default:
                    return false;
            }
        }
    }
}