using System;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>
    /// Utility helpers that produce compact text summaries for EffectDefinition entries.
    /// </summary>
    public static class EffectSummaryUtility
    {
        private const int BaseTurnSeconds = 6;

        public static string BuildSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            if (effectProp == null)
                return string.Empty;

            var typeProp = effectProp.FindPropertyRelative("effectType");
            if (typeProp == null)
                return string.Empty;

            var effectType = (EffectType)typeProp.enumValueIndex;
            return effectType switch
            {
                EffectType.Damage => BuildDamageSummary(effectProp, owningSkill),
                EffectType.Heal => BuildHealSummary(effectProp, owningSkill),
                EffectType.GainResource => BuildGainResourceSummary(effectProp, owningSkill),
                EffectType.ScalingBuff => BuildScalingBuffSummary(effectProp, owningSkill),
                EffectType.ApplyStatus => BuildApplyStatusSummary(effectProp, owningSkill),
                EffectType.ConditionalEffect => BuildConditionalSummary(effectProp),
                EffectType.ModifySkill => BuildModifySkillSummary(effectProp, owningSkill),
                EffectType.ReplaceSkill => BuildReplaceSkillSummary(effectProp, owningSkill),
                EffectType.Move => BuildMoveSummary(effectProp, owningSkill),
                EffectType.ModifyAction => BuildModifyActionSummary(effectProp, owningSkill),
                EffectType.ModifyDamageSchool => BuildModifyDamageSchoolSummary(effectProp, owningSkill),
                EffectType.AttributeModifier => BuildAttributeModifierSummary(effectProp, owningSkill),
                EffectType.MasteryPosture => BuildMasteryPostureSummary(effectProp, owningSkill),
                EffectType.CooldownModifier => BuildCooldownModifierSummary(effectProp, owningSkill),
                EffectType.RandomOutcome => BuildRandomOutcomeSummary(effectProp, owningSkill),
                EffectType.Repeat => BuildRepeatSummary(effectProp, owningSkill),
                EffectType.ProbabilityModifier => BuildProbabilityModifierSummary(effectProp, owningSkill),
                EffectType.DotHotModifier => BuildDotHotSummary(effectProp, owningSkill),
                _ => BuildDefaultSummary(effectProp, owningSkill, effectType)
            };
        }

        private static string BuildDamageSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            var sb = CreateHeader("Damage", GetTargetLabel(effectProp));
            AddValueLine(sb, effectProp, owningSkill, "Amount");
            var schoolProp = effectProp.FindPropertyRelative("damageSchool");
            if (schoolProp != null)
                AddBullet(sb, $"School: {((DamageSchool)schoolProp.enumValueIndex)}");

            var canCritProp = effectProp.FindPropertyRelative("canCrit");
            if (canCritProp != null)
                AddBullet(sb, $"Critical: {(canCritProp.boolValue ? "Yes" : "No")}");

            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }

        private static string BuildHealSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            var sb = CreateHeader("Heal", GetTargetLabel(effectProp));
            AddValueLine(sb, effectProp, owningSkill, "Amount");

            var canCritProp = effectProp.FindPropertyRelative("canCrit");
            if (canCritProp != null)
                AddBullet(sb, $"Critical: {(canCritProp.boolValue ? "Yes" : "No")}");

            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }

        private static string BuildGainResourceSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            string resource = GetEnumName(effectProp.FindPropertyRelative("resourceType"), ResourceType.Discipline);
            var sb = CreateHeader($"Gain Resource ({resource})", GetTargetLabel(effectProp));
            AddValueLine(sb, effectProp, owningSkill, "Amount");
            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }

        private static string BuildScalingBuffSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            var sb = CreateHeader("Scaling Buff", GetTargetLabel(effectProp));
            string resource = GetEnumName(effectProp.FindPropertyRelative("resourceType"), ResourceType.Discipline);
            string attribute = GetEnumName(effectProp.FindPropertyRelative("scalingAttribute"), ScalingAttribute.Attack);
            string operation = GetEnumName(effectProp.FindPropertyRelative("scalingOperation"), SkillModifyOperation.Add);
            string value = FormatSimpleString(effectProp.FindPropertyRelative("scalingValuePerResource"));
            AddBullet(sb, string.IsNullOrEmpty(value)
                ? "Value per resource: (not set)"
                : $"Value per resource: {value}");
            AddBullet(sb, $"Resource: {resource}");
            AddBullet(sb, $"Attribute: {attribute}");
            AddBullet(sb, $"Operation: {operation}");

            var maxStacksProp = effectProp.FindPropertyRelative("maxStacks");
            if (maxStacksProp != null)
            {
                AddBullet(sb, maxStacksProp.intValue <= 0
                    ? "Max stacks: Unlimited"
                    : $"Max stacks: {maxStacksProp.intValue}");
            }

            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }

        private static string BuildApplyStatusSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            string statusId = FormatSimpleString(effectProp.FindPropertyRelative("statusSkillID"));
            string title = string.IsNullOrEmpty(statusId) ? "Apply Status" : $"Apply Status '{statusId}'";
            var sb = CreateHeader(title, GetTargetLabel(effectProp));

            int? duration = ResolveDurationValue(effectProp, owningSkill);
            if (duration.HasValue)
            {
                string durationText = FormatDurationBullet(duration.Value, "Duration", forInstantTrigger: true);
                if (!string.IsNullOrEmpty(durationText))
                    AddBullet(sb, durationText);
            }

            string stacks = DescribeStacks(effectProp, owningSkill);
            if (!string.IsNullOrEmpty(stacks))
                AddBullet(sb, stacks);
            var maxStacksProp = effectProp.FindPropertyRelative("maxStacks");
            if (maxStacksProp != null && maxStacksProp.intValue > 0)
                AddBullet(sb, $"Max stacks: {maxStacksProp.intValue}");

            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }

        private static string BuildConditionalSummary(SerializedProperty effectProp)
        {
            var sb = CreateHeader("Conditional Effect");
            string resource = GetEnumName(effectProp.FindPropertyRelative("resourceType"), ResourceType.Discipline);
            string compare = GetEnumName(effectProp.FindPropertyRelative("compareOp"), CompareOp.Equal);
            float compareValue = effectProp.FindPropertyRelative("compareValue")?.floatValue ?? 0f;
            AddBullet(sb, $"Condition: {resource} {compare} {compareValue.ToString("0.###", CultureInfo.InvariantCulture)}");

            var onSuccess = effectProp.FindPropertyRelative("onSuccess");
            if (onSuccess != null)
                AddBullet(sb, $"On success: {onSuccess.arraySize} nested effect(s)");

            return sb.ToString().TrimEnd();
        }

        private static string BuildModifySkillSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            string skillLabel = GetSkillLabel(effectProp, owningSkill, "targetSkillID");
            var sb = CreateHeader($"Modify Skill ({skillLabel})");

            var modifyTypeProp = effectProp.FindPropertyRelative("skillModifyType");
            var operationProp = effectProp.FindPropertyRelative("skillModifyOperation");
            var modifierProp = effectProp.FindPropertyRelative("modifierType");

            SkillModifyType modifyType = modifyTypeProp != null
                ? (SkillModifyType)modifyTypeProp.enumValueIndex
                : SkillModifyType.None;
            SkillModifyOperation operation = operationProp != null
                ? (SkillModifyOperation)operationProp.enumValueIndex
                : SkillModifyOperation.Add;

            AddBullet(sb, $"Modify: {modifyType}");

            if (modifyType == SkillModifyType.CooldownReset)
            {
                bool refresh = effectProp.FindPropertyRelative("resetCooldownToMax")?.boolValue ?? true;
                AddBullet(sb, refresh
                    ? "Reset cooldown and start a new cycle"
                    : "Clear remaining cooldown without refreshing");
            }
            else
            {
                if (modifierProp != null)
                    AddBullet(sb, $"Modifier: {((ModifierType)modifierProp.enumValueIndex)}");

                AddBullet(sb, $"Operation: {operation}");
                AddValueLine(sb, effectProp, owningSkill, "Value");

                if (modifyType == SkillModifyType.ResourceCost)
                {
                    bool affectsAll = effectProp.FindPropertyRelative("modifyAffectsAllCosts")?.boolValue ?? true;
                    if (affectsAll)
                    {
                        AddBullet(sb, "Cost Target: All resources");
                    }
                    else
                    {
                        string costResource = GetEnumName(effectProp.FindPropertyRelative("modifyCostResource"), CostResourceType.Energy);
                        AddBullet(sb, $"Cost Target: {costResource}");
                    }
                }
            }

            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }

        private static string BuildReplaceSkillSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            string fromSkill = GetSkillLabel(effectProp, owningSkill, "targetSkillID");
            var sb = CreateHeader($"Replace Skill ({fromSkill})");

            string toSkill = FormatSimpleString(effectProp.FindPropertyRelative("replaceSkillID"));
            AddBullet(sb, string.IsNullOrEmpty(toSkill)
                ? "New skill: (missing ID)"
                : $"New skill: skill '{toSkill}'");

            bool inherit = effectProp.FindPropertyRelative("inheritReplacedCooldown")?.boolValue ?? true;
            AddBullet(sb, $"Inherit cooldown: {(inherit ? "Yes" : "No")}");

            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }

        private static string BuildMoveSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            string subject = GetEnumName(effectProp.FindPropertyRelative("moveSubject"), MoveSubject.Caster);
            var sb = CreateHeader($"Move ({subject})", GetTargetLabel(effectProp));

            string execution = GetEnumName(effectProp.FindPropertyRelative("moveExecution"), MoveExecution.Step);
            AddBullet(sb, $"Execution: {execution}");

            var directionProp = effectProp.FindPropertyRelative("moveDirection");
            MoveDirection direction = directionProp != null
                ? (MoveDirection)directionProp.enumValueIndex
                : MoveDirection.Forward;

            switch (direction)
            {
                case MoveDirection.AbsoluteOffset:
                    var offset = effectProp.FindPropertyRelative("moveOffset")?.vector2IntValue ?? Vector2Int.zero;
                    AddBullet(sb, $"Direction: Absolute offset ({offset.x}, {offset.y})");
                    break;
                default:
                    int distance = effectProp.FindPropertyRelative("moveDistance")?.intValue ?? 0;
                    AddBullet(sb, $"Direction: {direction} ({distance} tile(s))");
                    int maxDistance = effectProp.FindPropertyRelative("moveMaxDistance")?.intValue ?? 0;
                    if (direction == MoveDirection.TowardTarget && maxDistance > 0)
                        AddBullet(sb, $"Max distance: {maxDistance} tile(s)");
                    break;
            }

            bool stopAdjacent = effectProp.FindPropertyRelative("moveStopAdjacentToTarget")?.boolValue ?? false;
            bool force = effectProp.FindPropertyRelative("forceMovement")?.boolValue ?? false;
            bool allowPartial = effectProp.FindPropertyRelative("allowPartialMove")?.boolValue ?? false;
            bool ignoreObstacles = effectProp.FindPropertyRelative("moveIgnoreObstacles")?.boolValue ?? false;

            var flagSb = new StringBuilder();
            if (force) flagSb.Append("Force movement");
            if (allowPartial)
            {
                if (flagSb.Length > 0) flagSb.Append(", ");
                flagSb.Append("Allow partial move");
            }
            if (ignoreObstacles)
            {
                if (flagSb.Length > 0) flagSb.Append(", ");
                flagSb.Append("Ignore obstacles");
            }
            if (stopAdjacent)
            {
                if (flagSb.Length > 0) flagSb.Append(", ");
                flagSb.Append("Stop adjacent to target");
            }
            if (flagSb.Length > 0)
                AddBullet(sb, flagSb.ToString());

            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }

        private static string BuildModifyActionSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            string skillLabel = GetSkillLabel(effectProp, owningSkill, "targetSkillID");
            var sb = CreateHeader($"Modify Action ({skillLabel})");

            string actionFilter = DescribeActionFilter(effectProp.FindPropertyRelative("targetActionType"));
            if (!string.IsNullOrEmpty(actionFilter))
                AddBullet(sb, $"Filter: {actionFilter}");

            var modifyTypeProp = effectProp.FindPropertyRelative("actionModifyType");
            ActionModifyType modifyType = modifyTypeProp != null
                ? (ActionModifyType)modifyTypeProp.enumValueIndex
                : ActionModifyType.None;

            AddBullet(sb, $"Modify: {modifyType}");

            if (modifyType == ActionModifyType.Damage)
            {
                var modifierProp = effectProp.FindPropertyRelative("modifierType");
                if (modifierProp != null)
                    AddBullet(sb, $"Modifier: {((ModifierType)modifierProp.enumValueIndex)}");
                AddValueLine(sb, effectProp, owningSkill, "Value");
            }
            else if (modifyType == ActionModifyType.ActionType)
            {
                string newType = DescribeActionFilter(effectProp.FindPropertyRelative("actionTypeOverride"));
                if (!string.IsNullOrEmpty(newType))
                    AddBullet(sb, $"Convert to: {newType}");
            }

            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }
        private static string BuildModifyDamageSchoolSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            string skillLabel = GetSkillLabel(effectProp, owningSkill, "targetSkillID");
            var sb = CreateHeader($"Modify Damage School ({skillLabel})");

            string school = GetEnumName(effectProp.FindPropertyRelative("damageSchool"), DamageSchool.Physical);
            AddBullet(sb, $"School: {school}");

            var modifierProp = effectProp.FindPropertyRelative("modifierType");
            if (modifierProp != null)
                AddBullet(sb, $"Modifier: {((ModifierType)modifierProp.enumValueIndex)}");

            var opProp = effectProp.FindPropertyRelative("skillModifyOperation");
            if (opProp != null)
                AddBullet(sb, $"Operation: {((SkillModifyOperation)opProp.enumValueIndex)}");

            AddValueLine(sb, effectProp, owningSkill, "Modifier");
            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }


        private static string BuildAttributeModifierSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            string target = GetTargetLabel(effectProp);
            var sb = CreateHeader("Attribute Modifier", target);

            string attribute = GetEnumName(effectProp.FindPropertyRelative("attributeType"), AttributeType.Attack);
            AddBullet(sb, $"Attribute: {attribute}");

            var modifierProp = effectProp.FindPropertyRelative("modifierType");
            if (modifierProp != null)
                AddBullet(sb, $"Modifier: {((ModifierType)modifierProp.enumValueIndex)}");

            AddValueLine(sb, effectProp, owningSkill, "Value");

            string stacks = DescribeStacks(effectProp, owningSkill);
            if (!string.IsNullOrEmpty(stacks))
                AddBullet(sb, stacks);

            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }

        private static string BuildMasteryPostureSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            var settings = effectProp.FindPropertyRelative("masteryPosture");
            if (settings == null)
                return string.Empty;

            var sb = CreateHeader("Mastery Posture");

            bool lockArmor = settings.FindPropertyRelative("lockArmorToZero")?.boolValue ?? true;
            AddBullet(sb, $"Lock armor to zero: {(lockArmor ? "Yes" : "No")}");

            float hpRatio = settings.FindPropertyRelative("armorToHpRatio")?.floatValue ?? 0f;
            float energyRatio = settings.FindPropertyRelative("armorToEnergyRatio")?.floatValue ?? 0f;
            AddBullet(sb, $"Armor conversion → HP: {hpRatio.ToString("P0", CultureInfo.InvariantCulture)}, Max Energy: {energyRatio.ToString("P0", CultureInfo.InvariantCulture)}");

            string postureResource = GetEnumName(settings.FindPropertyRelative("postureResource"), ResourceType.posture);
            AddBullet(sb, $"Posture resource: {postureResource}");

            float postureRatio = settings.FindPropertyRelative("postureMaxHealthRatio")?.floatValue ?? 0f;
            string postureOverride = FormatSimpleString(settings.FindPropertyRelative("postureMaxExpression"));
            if (!string.IsNullOrEmpty(postureOverride))
                AddBullet(sb, $"Max posture: {postureOverride}");
            else
                AddBullet(sb, $"Max posture: {postureRatio.ToString("0.##", CultureInfo.InvariantCulture)}× HP");

            string masteryScale = FormatSimpleString(settings.FindPropertyRelative("masteryScalingExpression"));
            if (!string.IsNullOrEmpty(masteryScale))
                AddBullet(sb, $"Mastery scaling: {masteryScale}");

            AddValueLine(sb, effectProp, owningSkill, "Damage → Posture");

            float recovery = settings.FindPropertyRelative("postureRecoveryPercentPerTurn")?.floatValue ?? 0f;
            AddBullet(sb, $"Recovery per turn: {recovery.ToString("P0", CultureInfo.InvariantCulture)} of missing posture");

            float breakMultiplier = settings.FindPropertyRelative("postureBreakExtraDamageMultiplier")?.floatValue ?? 0f;
            string breakStatus = FormatSimpleString(settings.FindPropertyRelative("postureBreakStatusSkillID"));
            int breakDuration = settings.FindPropertyRelative("postureBreakDurationTurns")?.intValue ?? 0;
            bool skipTurn = settings.FindPropertyRelative("postureBreakSkipsTurn")?.boolValue ?? true;

            AddBullet(sb,
                $"Break: {breakMultiplier.ToString("0.##", CultureInfo.InvariantCulture)}× damage, status {(string.IsNullOrEmpty(breakStatus) ? "(none)" : $"'{breakStatus}'")} for {breakDuration} turn(s), skip next turn: {(skipTurn ? "Yes" : "No")}");

            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }

        private static string BuildCooldownModifierSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            var sb = CreateHeader("Cooldown Modifier");
            AddBullet(sb, $"Scope: {DescribeCooldownScope(effectProp, owningSkill)}");

            int seconds = effectProp.FindPropertyRelative("cooldownChangeSeconds")?.intValue ?? 0;
            int rounds = 0;
            if (seconds != 0)
            {
                int abs = Mathf.CeilToInt(Mathf.Abs(seconds) / (float)BaseTurnSeconds);
                abs = Mathf.Max(abs, 1);
                rounds = seconds > 0 ? abs : -abs;
            }
            string secondsText = seconds >= 0 ? $"+{seconds}" : seconds.ToString();
            string roundsText = rounds >= 0 ? $"+{rounds}" : rounds.ToString();
            AddBullet(sb, $"Change: {secondsText}s ({roundsText} round(s))");

            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }
        private static string BuildRandomOutcomeSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            var sb = CreateHeader("Random Outcome", GetTargetLabel(effectProp));
            int rollCount = effectProp.FindPropertyRelative("randomRollCount")?.intValue ?? 1;
            bool allowDuplicates = effectProp.FindPropertyRelative("randomAllowDuplicates")?.boolValue ?? true;
            AddBullet(sb, $"Roll count: {rollCount}");
            AddBullet(sb, $"Allow duplicates: {(allowDuplicates ? "Yes" : "No")}");

            var outcomes = effectProp.FindPropertyRelative("randomOutcomes");
            if (outcomes != null)
            {
                int count = outcomes.arraySize;
                AddBullet(sb, $"Options: {count}");
                for (int i = 0; i < count; i++)
                {
                    var entry = outcomes.GetArrayElementAtIndex(i);
                    string label = FormatSimpleString(entry.FindPropertyRelative("label"));
                    if (string.IsNullOrEmpty(label))
                        label = $"Option {i + 1}";
                    int weight = entry.FindPropertyRelative("weight")?.intValue ?? 0;
                    string mode = GetEnumName(entry.FindPropertyRelative("probabilityMode"), ProbabilityModifierMode.None);
                    AddBullet(sb, $"{label} (Weight {weight}, Mode {mode})");
                }
            }

            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }

        private static string BuildRepeatSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            var sb = CreateHeader("Repeat Effects", GetTargetLabel(effectProp));
            var sourceProp = effectProp.FindPropertyRelative("repeatCountSource");
            RepeatCountSource source = sourceProp != null
                ? (RepeatCountSource)sourceProp.enumValueIndex
                : RepeatCountSource.Fixed;
            AddBullet(sb, $"Count source: {source}");

            int baseCount = effectProp.FindPropertyRelative("repeatCount")?.intValue ?? 0;
            AddBullet(sb, $"Base count: {baseCount}");

            if (source == RepeatCountSource.Expression)
            {
                string expr = FormatSimpleString(effectProp.FindPropertyRelative("repeatCountExpression"));
                if (!string.IsNullOrEmpty(expr))
                    AddBullet(sb, $"Expression: {expr}");
            }

            if (source == RepeatCountSource.ResourceValue || source == RepeatCountSource.ResourceSpent)
            {
                string resource = GetEnumName(effectProp.FindPropertyRelative("repeatResourceType"), ResourceType.Discipline);
                AddBullet(sb, $"Resource: {resource}");
            }

            bool consume = effectProp.FindPropertyRelative("repeatConsumeResource")?.boolValue ?? true;
            AddBullet(sb, $"Consume resource: {(consume ? "Yes" : "No")}");

            int maxCount = effectProp.FindPropertyRelative("repeatMaxCount")?.intValue ?? 0;
            if (maxCount > 0)
                AddBullet(sb, $"Max count: {maxCount}");

            var nested = effectProp.FindPropertyRelative("repeatEffects");
            if (nested != null)
                AddBullet(sb, $"Nested effects: {nested.arraySize}");

            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }

        private static string BuildProbabilityModifierSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            var sb = CreateHeader("Probability Modifier", GetTargetLabel(effectProp));
            string mode = GetEnumName(effectProp.FindPropertyRelative("probabilityModifierMode"), ProbabilityModifierMode.None);
            AddBullet(sb, $"Mode: {mode}");
            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }

        private static string BuildDotHotSummary(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            var sb = CreateHeader("DoT / HoT Modifier", GetTargetLabel(effectProp));
            DotHotOperation operation = effectProp.FindPropertyRelative("dotHotOperation") != null
                ? (DotHotOperation)effectProp.FindPropertyRelative("dotHotOperation").enumValueIndex
                : DotHotOperation.TriggerDots;
            AddBullet(sb, $"Operation: {operation}");

            int trigger = effectProp.FindPropertyRelative("dotHotBaseTriggerCount")?.intValue ?? 0;
            string triggerLabel = trigger == 0 ? "default (6s baseline)" : trigger.ToString();
            AddBullet(sb, $"Base trigger count: {triggerLabel}");

            if (operation != DotHotOperation.None)
                AddValueLine(sb, effectProp, owningSkill, "Tick Value");

            int mask = effectProp.FindPropertyRelative("visibleFields")?.intValue ?? 0;
            bool showSchool = (mask & (int)EffectFieldMask.School) != 0;
            bool showCrit = (mask & (int)EffectFieldMask.Crit) != 0;

            if (showSchool && (operation == DotHotOperation.TriggerDots || operation == DotHotOperation.ConvertDamageToDot))
            {
                string school = GetEnumName(effectProp.FindPropertyRelative("damageSchool"), DamageSchool.Physical);
                AddBullet(sb, $"Damage school: {school}");
            }

            if (showCrit && operation != DotHotOperation.None)
            {
                bool canCrit = effectProp.FindPropertyRelative("canCrit")?.boolValue ?? false;
                AddBullet(sb, $"Can crit: {(canCrit ? "Yes" : "No")}");
            }

            var extras = effectProp.FindPropertyRelative("dotHotAdditionalEffects");
            if (extras != null)
                AddBullet(sb, $"Additional effects: {extras.arraySize}");

            AppendCommonLines(sb, effectProp, owningSkill, durationInRounds: true);
            return sb.ToString().TrimEnd();
        }

        private static string BuildDefaultSummary(SerializedProperty effectProp, SkillDefinition owningSkill, EffectType effectType)
        {
            string target = effectProp.FindPropertyRelative("target") != null ? GetTargetLabel(effectProp) : null;
            var sb = CreateHeader(effectType.ToString(), target);
            AddValueLine(sb, effectProp, owningSkill, "Value");
            AppendCommonLines(sb, effectProp, owningSkill);
            return sb.ToString().TrimEnd();
        }

        private static StringBuilder CreateHeader(string title, string target = null)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(target))
                sb.AppendLine($"{title} → Target: {target}");
            else
                sb.AppendLine(title);
            return sb;
        }

        private static void AddBullet(StringBuilder sb, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            sb.AppendLine($"• {text}");
        }

        private static void AddValueLine(StringBuilder sb, SerializedProperty effectProp, SkillDefinition owningSkill, string label)
        {
            string value = DescribeValue(effectProp, owningSkill, label);
            if (!string.IsNullOrEmpty(value))
                AddBullet(sb, value);
        }

        private static void AppendCommonLines(StringBuilder sb, SerializedProperty effectProp, SkillDefinition owningSkill, bool durationInRounds = false)
        {
            AddBullet(sb, DescribeDuration(effectProp, owningSkill, durationInRounds));
            AddBullet(sb, DescribeProbability(effectProp, owningSkill));
            AddBullet(sb, DescribeCondition(effectProp));
        }
        private static string DescribeCooldownScope(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            var scopeProp = effectProp.FindPropertyRelative("cooldownTargetScope");
            CooldownTargetScope scope = scopeProp != null
                ? (CooldownTargetScope)scopeProp.enumValueIndex
                : CooldownTargetScope.Self;

            switch (scope)
            {
                case CooldownTargetScope.All:
                    return "All skills";
                case CooldownTargetScope.ExceptRed:
                    return "All non-ultimate (non-Red) skills";
                default:
                    string displayName = GetSkillDisplayName(owningSkill);
                    return string.IsNullOrEmpty(displayName)
                        ? "Self"
                        : $"Self ({displayName})";
            }
        }

        private static string DescribeValue(SerializedProperty effectProp, SkillDefinition owningSkill, string label)
        {
            int level = GetSkillLevel(owningSkill);
            bool perLevel = effectProp.FindPropertyRelative("perLevel")?.boolValue ?? false;

            if (perLevel)
            {
                var perLevelArray = effectProp.FindPropertyRelative("valueExprLevels");
                string perLevelValue = GetStringFromArray(perLevelArray, level);
                if (!string.IsNullOrWhiteSpace(perLevelValue))
                    return $"{label} @L{level}: {perLevelValue}";
            }

            var valueProp = FieldVisibilityUI.GetProp(effectProp, "valueExpression", "value");
            if (valueProp != null)
            {
                string formatted = FormatPropertyValue(valueProp);
                if (!string.IsNullOrEmpty(formatted))
                    return perLevel
                        ? $"{label} (base): {formatted}"
                        : $"{label}: {formatted}";
            }

            return string.Empty;
        }

        private static string DescribeStacks(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            int level = GetSkillLevel(owningSkill);
            bool perLevel = effectProp.FindPropertyRelative("perLevel")?.boolValue ?? false;

            if (perLevel)
            {
                var perLevelStacks = effectProp.FindPropertyRelative("stackCountLevels");
                int levelStacks = GetIntFromArray(perLevelStacks, level);
                if (levelStacks > 0)
                    return $"Stacks @L{level}: {levelStacks}";
            }

            var baseStacks = effectProp.FindPropertyRelative("stackCount");
            if (baseStacks != null && baseStacks.intValue > 1)
                return $"Stacks: {baseStacks.intValue}";

            return string.Empty;
        }

        private static string DescribeDuration(SerializedProperty effectProp, SkillDefinition owningSkill, bool useRounds = false)
        {
            int mask = effectProp.FindPropertyRelative("visibleFields")?.intValue ?? 0;
            bool useCustom = (mask & (int)EffectFieldMask.Duration) != 0;

            if (!useCustom)
            {
                if (owningSkill != null)
                {
                    int resolved = owningSkill.ResolveDuration(GetSkillLevel(owningSkill));
                    string resolvedLabel = FormatDurationLabel(resolved, useRounds);
                    if (!string.IsNullOrEmpty(resolvedLabel))
                        return $"Duration: follows skill ({resolvedLabel})";
                }
                return string.Empty;
            }

            int level = GetSkillLevel(owningSkill);
            bool perLevel = UsesPerLevelDuration(effectProp);

            if (perLevel)
            {
                var perLevelArray = effectProp.FindPropertyRelative("durationLevels");
                int perLevelValue = GetIntFromArray(perLevelArray, level);
                string perLevelLabel = FormatDurationLabel(perLevelValue, useRounds);
                return $"Duration @L{level}: {perLevelLabel}";
            }

            var durationProp = effectProp.FindPropertyRelative("duration");
            if (durationProp != null)
            {
                int rawValue = durationProp.propertyType == SerializedPropertyType.Integer
                   ? durationProp.intValue
                    : Mathf.RoundToInt(durationProp.floatValue);
                string label = FormatDurationLabel(rawValue, useRounds);
                if (!string.IsNullOrEmpty(label))
                    return $"Duration: {label}";
            }

            return string.Empty;
        }

        private static string FormatDurationBullet(int duration, string prefix, bool forInstantTrigger = false, bool useRounds = false)
        {
            if (duration > 0)
                return $"{prefix}: {duration} {(useRounds ? "round(s)" : "turn(s)")}";
            if (duration == -1)
                return forInstantTrigger ? "Instant trigger (fires status effects immediately)" : $"{prefix}: Instant";
            if (duration == -2)
                return $"{prefix}: Permanent";
            if (duration == 0)
                return string.Empty;
            return $"{prefix}: {duration}";
        }

        private static string FormatDurationLabel(int duration, bool useRounds = false)
        {
            if (duration > 0)
                return $"{duration} turn(s)";
            if (duration == -1)
                return "Instant";
            if (duration == -2)
                return "Permanent";
            if (duration == 0)
                return string.Empty;
            return duration.ToString();
        }

        private static string DescribeProbability(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            int mask = effectProp.FindPropertyRelative("visibleFields")?.intValue ?? 0;
            bool show = (mask & (int)EffectFieldMask.Probability) != 0;
            if (!show)
                return string.Empty;

            int level = GetSkillLevel(owningSkill);
            bool perLevel = effectProp.FindPropertyRelative("perLevel")?.boolValue ?? false;

            string value = string.Empty;
            if (perLevel)
            {
                var perLevelArray = effectProp.FindPropertyRelative("probabilityLvls");
                value = GetStringFromArray(perLevelArray, level);
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                value = effectProp.FindPropertyRelative("probability")?.stringValue;
            }

            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value.Trim();
            if (!value.EndsWith("%"))
            {
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float numeric))
                    value = numeric.ToString("0.###", CultureInfo.InvariantCulture) + "%";
                else
                    value += "%";
            }

            return perLevel
                ? $"Chance @L{level}: {value}"
                : $"Chance: {value}";
        }

        private static string DescribeCondition(SerializedProperty effectProp)
        {
            var conditionProp = effectProp.FindPropertyRelative("condition");
            if (conditionProp == null)
                return string.Empty;

            var condition = (EffectCondition)conditionProp.enumValueIndex;
            if (condition == EffectCondition.None)
                return string.Empty;

            var sb = new StringBuilder();
            sb.Append("Condition: ");
            sb.Append(condition);

            switch (condition)
            {
                case EffectCondition.AfterSkillUse:
                    string skillId = effectProp.FindPropertyRelative("conditionSkillUseID")?.stringValue;
                    if (string.IsNullOrWhiteSpace(skillId) || string.Equals(skillId, "any", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(" (skill: any)");
                    }
                    else
                    {
                        sb.Append($" (skill: '{skillId}')");
                    }
                    break;
                case EffectCondition.SkillStateActive:
                    string stateSkillId = effectProp.FindPropertyRelative("conditionSkillStateID")?.stringValue;
                    if (string.IsNullOrWhiteSpace(stateSkillId))
                        sb.Append(" (state: any)");
                    else
                        sb.Append($" (state: '{stateSkillId}')");
                    break;
                case EffectCondition.OnNextSkillSpendResource:
                    string resource = GetEnumName(effectProp.FindPropertyRelative("conditionResourceType"), ResourceType.Discipline);
                    int minAmount = effectProp.FindPropertyRelative("conditionMinAmount")?.intValue ?? 1;
                    bool consume = effectProp.FindPropertyRelative("consumeStatusOnTrigger")?.boolValue ?? true;
                    sb.Append($" (resource: {resource}, ≥{minAmount}");
                    if (consume) sb.Append(", consumes status");
                    sb.Append(')');
                    break;
                case EffectCondition.OnDamageTaken:
                    sb.Append(" (trigger: when damage is taken)");
                    break;
                case EffectCondition.OnEffectEnd:
                    sb.Append(" (trigger: when the effect ends)");
                    break;
            }

            return sb.ToString();
        }

        private static string GetTargetLabel(SerializedProperty effectProp)
        {
            var targetProp = effectProp.FindPropertyRelative("target");
            if (targetProp == null)
                return "Self";
            return ((TargetType)targetProp.enumValueIndex).ToString();
        }

        private static string GetSkillLabel(SerializedProperty effectProp, SkillDefinition owningSkill, string propertyName)
        {
            var prop = effectProp.FindPropertyRelative(propertyName);
            string id = prop != null ? prop.stringValue : string.Empty;
            if (!string.IsNullOrWhiteSpace(id))
                return $"skill '{id}'";

            if (owningSkill != null && !string.IsNullOrWhiteSpace(owningSkill.skillID))
                return $"skill '{owningSkill.skillID}'";

            return "owning skill";
        }

        private static string GetSkillDisplayName(SkillDefinition skill)
        {
            if (skill == null)
                return string.Empty;

            bool hasName = !string.IsNullOrWhiteSpace(skill.skillName);
            bool hasId = !string.IsNullOrWhiteSpace(skill.skillID);

            if (hasName && hasId)
                return $"{skill.skillName} [{skill.skillID}]";
            if (hasName)
                return skill.skillName;
            if (hasId)
                return skill.skillID;

            return string.Empty;
        }
        private static string DescribeActionFilter(SerializedProperty actionProp)
        {
            if (actionProp == null)
                return string.Empty;

            var action = (ActionType)actionProp.enumValueIndex;
            if (action == ActionType.None)
                return "All actions";
            return action.ToString();
        }
        private static bool UsesPerLevelDuration(SerializedProperty effectProp)
        {
            var perLevelDurationProp = effectProp.FindPropertyRelative("perLevelDuration");
            if (perLevelDurationProp != null)
                return perLevelDurationProp.boolValue;
            return effectProp.FindPropertyRelative("perLevel")?.boolValue ?? false;
        }

        private static int GetSkillLevel(SkillDefinition owningSkill)
        {
            if (owningSkill == null)
                return 1;
            return Mathf.Clamp(owningSkill.skillLevel, 1, 4);
        }

        private static string FormatPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.String:
                    return string.IsNullOrWhiteSpace(prop.stringValue) ? string.Empty : prop.stringValue.Trim();
                case SerializedPropertyType.Float:
                    return prop.floatValue.ToString("0.###", CultureInfo.InvariantCulture);
                case SerializedPropertyType.Integer:
                    return prop.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:
                    return prop.boolValue ? "true" : "false";
                default:
                    return string.Empty;
            }
        }

        private static string FormatSimpleString(SerializedProperty prop)
        {
            return prop == null ? string.Empty : prop.stringValue;
        }

        private static string GetStringFromArray(SerializedProperty arrayProp, int level)
        {
            if (arrayProp == null || !arrayProp.isArray || arrayProp.arraySize == 0)
                return string.Empty;
            int idx = Mathf.Clamp(level - 1, 0, arrayProp.arraySize - 1);
            var element = arrayProp.GetArrayElementAtIndex(idx);
            return element.propertyType == SerializedPropertyType.String ? element.stringValue : string.Empty;
        }

        private static int GetIntFromArray(SerializedProperty arrayProp, int level)
        {
            if (arrayProp == null || !arrayProp.isArray || arrayProp.arraySize == 0)
                return 0;
            int idx = Mathf.Clamp(level - 1, 0, arrayProp.arraySize - 1);
            var element = arrayProp.GetArrayElementAtIndex(idx);
            return element.propertyType == SerializedPropertyType.Integer ? element.intValue : 0;
        }

        private static int? ResolveDurationValue(SerializedProperty effectProp, SkillDefinition owningSkill)
        {
            int mask = effectProp.FindPropertyRelative("visibleFields")?.intValue ?? 0;
            bool useCustom = (mask & (int)EffectFieldMask.Duration) != 0;

            if (!useCustom)
            {
                if (owningSkill == null)
                    return null;
                return owningSkill.ResolveDuration(GetSkillLevel(owningSkill));
            }

            int level = GetSkillLevel(owningSkill);

            bool perLevel = UsesPerLevelDuration(effectProp);
            if (perLevel)
            {
                int perLevelValue = GetIntFromArray(effectProp.FindPropertyRelative("durationLevels"), level);
                if (perLevelValue != 0)
                    return perLevelValue;
            }

            var durationProp = effectProp.FindPropertyRelative("duration");
            if (durationProp != null)
            {
                return durationProp.propertyType == SerializedPropertyType.Integer
                    ? durationProp.intValue
                    : Mathf.RoundToInt(durationProp.floatValue);
            }

            return null;
        }

        private static string GetEnumName<TEnum>(SerializedProperty prop, TEnum fallback) where TEnum : struct, System.Enum
        {
            if (prop == null)
                return fallback.ToString();
            int index = prop.enumValueIndex;
            var values = (TEnum[])System.Enum.GetValues(typeof(TEnum));
            if (index >= 0 && index < values.Length)
                return values[index].ToString();
            return fallback.ToString();
        }
    }
}