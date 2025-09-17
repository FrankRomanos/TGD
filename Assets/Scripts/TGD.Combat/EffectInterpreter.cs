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
                    ProfessionScaling = workingContext.ProfessionScaling,
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
                    var preview = new SkillUseConditionPreview
                    {
                        Resource = condition.resourceType,
                        Comparison = condition.compareOp,
                        CompareValue = condition.compareValue
                    };
                    result.SkillUseConditions.Add(preview);
                    result.AddLog($"Use condition: {condition.resourceType} {condition.compareOp} {condition.compareValue}.");
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

        private static void InterpretEffect(EffectDefinition effect, EffectContext context, EffectInterpretationResult result, HashSet<string> visited)
        {
            if (effect == null)
                return;

            if (!CheckCondition(effect, context))
            {
                result.AddLog($"Condition {effect.condition} not met for {effect.effectType}.");
                return;
            }

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
                case EffectType.ApplyStatus:
                    ApplyStatus(effect, context, result, visited);
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
                case EffectType.AttributeModifier:
                    ApplyAttributeModifier(effect, context, result);
                    break;
                case EffectType.MasteryPosture:
                    ApplyMasteryPosture(effect, context, result);
                    break;
                case EffectType.CooldownModifier:
                    ApplyCooldownModifier(effect, context, result);
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
                result.Damage.Add(new DamagePreview
                {
                    Source = context.Caster,
                    Target = target,
                    Amount = amount,
                    School = effect.damageSchool,
                    CanCrit = effect.canCrit,
                    Probability = probability,
                    Expression = expression,
                    Condition = effect.condition
                });
                result.AddLog($"Damage {DescribeUnit(target)} for {amount:0.##} {effect.damageSchool} ({probability:0.##}% chance).");
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
                Target = effect.target,
                Probability = probability,
                Condition = effect.condition
            });
            result.AddLog($"Scaling buff: {effect.scalingAttribute} per {effect.resourceType} ({effect.scalingValuePerResource}).");
        }

        private static void ApplyStatus(EffectDefinition effect, EffectContext context, EffectInterpretationResult result, HashSet<string> visited)
        {
            var targets = ResolveTargets(effect.target, context);
            float probability = ResolveProbabilityValue(effect, context, targets.Count > 0 ? targets[0] : context.PrimaryTarget);
            int duration = ResolveDuration(effect, context);
            int stacks = ResolveStackCount(effect, context);
            bool isInstant = duration == -1;

            var preview = new StatusApplicationPreview
            {
                StatusSkillID = effect.statusSkillID,
                Duration = duration,
                StackCount = stacks,
                IsInstant = isInstant,
                Target = effect.target,
                Probability = probability,
                Condition = effect.condition
            };

            if (isInstant && !string.IsNullOrWhiteSpace(effect.statusSkillID) && context.SkillResolver != null)
            {
                var statusSkill = context.SkillResolver.FindSkill(effect.statusSkillID);
                if (statusSkill != null)
                {
                    var childContext = context.CloneForSkill(statusSkill, inheritSkillLevelOverride: false);
                    preview.InstantResult = InterpretSkillInternal(childContext, visited);
                    result.Append(preview.InstantResult);
                }
                else
                {
                    result.AddLog($"Status skill '{effect.statusSkillID}' not found for ApplyStatus effect.");
                }
            }

            result.StatusApplications.Add(preview);
            string action = isInstant ? "Trigger" : "Apply";
            result.AddLog($"{action} status '{effect.statusSkillID}' ({stacks} stack(s)) for {duration} turn(s) ({probability:0.##}% chance).");
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

            var preview = new SkillModificationPreview
            {
                TargetSkillID = targetSkill,
                ModifyType = effect.skillModifyType,
                Operation = effect.skillModifyOperation,
                ModifierType = effect.modifierType,
                ValueExpression = ResolveValueExpression(effect, context),
                AffectAllCosts = effect.modifyAffectsAllCosts,
                CostResource = effect.modifyCostResource,
                Probability = probability,
                Condition = effect.condition
            };

            if (effect.skillModifyType == SkillModifyType.CooldownReset)
            {
                preview.ValueExpression = effect.resetCooldownToMax ? "Reset" : "Clear";
            }

            result.SkillModifications.Add(preview);
            result.AddLog($"Modify skill '{targetSkill}' ({effect.skillModifyType}).");
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

        private static void ApplyAttributeModifier(EffectDefinition effect, EffectContext context, EffectInterpretationResult result)
        {
            float probability = ResolveProbabilityValue(effect, context, context.Caster);
            int duration = ResolveDuration(effect, context);
            int stacks = ResolveStackCount(effect, context);
            string expression = ResolveValueExpression(effect, context);

            result.AttributeModifiers.Add(new AttributeModifierPreview
            {
                Attribute = effect.attributeType,
                ModifierType = effect.modifierType,
                ValueExpression = expression,
                Duration = duration,
                StackCount = stacks,
                Target = effect.target,
                Probability = probability,
                Condition = effect.condition
            });
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
            int rounds = 0;
            if (seconds != 0)
            {
                int abs = Mathf.CeilToInt(Mathf.Abs(seconds) / (float)CombatClock.BaseTurnSeconds);
                abs = Mathf.Max(abs, 1);
                rounds = seconds > 0 ? abs : -abs;
            }

            float probability = ResolveProbabilityValue(effect, context, context.Caster);
            string selfSkillId = context.Skill != null ? context.Skill.skillID : string.Empty;
            var scope = effect.cooldownTargetScope;
            result.CooldownModifications.Add(new CooldownModificationPreview
            {
                Scope = scope,
                SelfSkillID = selfSkillId,
                Seconds = seconds,
                Rounds = rounds,
                Probability = probability,
                Condition = effect.condition
            });

            string scopeDescription = scope switch
            {
                CooldownTargetScope.All => "all skills",
                CooldownTargetScope.ExceptRed => "all non-ultimate (non-Red) skills",
                _ => !string.IsNullOrWhiteSpace(selfSkillId) ? $"skill '{selfSkillId}'" : "own skill"
            };

            result.AddLog($"Modify cooldown for {scopeDescription} by {seconds:+#;-#;0}s ({rounds:+#;-#;0} rounds).");
        }

        private static bool CheckCondition(EffectDefinition effect, EffectContext context)
        {
            switch (effect.condition)
            {
                case EffectCondition.None:
                    return true;
                case EffectCondition.AfterAttack:
                    return context.ConditionAfterAttack;
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
                    return context.ConditionSkillStateActive;
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

            if (context.Caster != null)
                AddStats(map, context.Caster.Stats, string.Empty);

            if (target != null)
                AddStats(map, target.Stats, "target_");

            map["p"] = context.ProfessionScaling;
            map["damage"] = context.IncomingDamage;
            map["damage_pre"] = context.IncomingDamage;
            map["damage_post"] = context.IncomingDamageMitigated;
            map["spent"] = context.LastResourceSpendAmount;

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
            map[prefix + "mastery"] = stats.Mastery;
            map[prefix + "posture"] = stats.Posture;
            map[prefix + "maxposture"] = stats.MaxPosture;
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