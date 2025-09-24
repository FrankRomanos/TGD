using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TGD.Data;

namespace TGD.Combat
{
    public sealed class AuraSystem : IAuraSystem
    {
        readonly ICombatLogger _logger;

        public AuraSystem(ICombatLogger logger)
        {
            _logger = logger;
        }

        public void Execute(AuraOp op, RuntimeCtx ctx)
        {
            if (op == null)
                return;

            var anchor = op.AnchorUnit ?? ctx?.Caster;
            if (anchor == null)
                return;

            var targets = FilterTargets(anchor, op, ctx).ToArray();
            if (targets.Length == 0)
                return;

            ApplyAdditionalOps(op, targets, ctx);
            _logger?.Log("AURA_SPAWN", anchor.UnitId, op.RangeMode, targets.Length);
        }

        IEnumerable<Unit> FilterTargets(Unit anchor, AuraOp op, RuntimeCtx ctx)
        {
            var candidates = CollectCandidates(op.AffectedTargets, ctx, anchor);
            foreach (var unit in candidates)
            {
                if (unit == null)
                    continue;
                if (op.AffectsImmune == false && unit.IsEnemyOf(anchor) && op.Category == AuraEffectCategory.Buff)
                    continue;

                float distance = Vector2Int.Distance(unit.Position, anchor.Position);
                if (MatchesRange(distance, op))
                    yield return unit;
            }
        }

        IEnumerable<Unit> CollectCandidates(TargetType targetType, RuntimeCtx ctx, Unit anchor)
        {
            switch (targetType)
            {
                case TargetType.Self:
                    yield return anchor;
                    break;
                case TargetType.Enemy:
                    if (ctx?.Enemies != null)
                        foreach (var enemy in ctx.Enemies)
                            yield return enemy;
                    break;
                case TargetType.Allies:
                    if (ctx?.Allies != null)
                        foreach (var ally in ctx.Allies)
                            yield return ally;
                    break;
                case TargetType.All:
                    if (ctx?.Allies != null)
                        foreach (var ally in ctx.Allies)
                            yield return ally;
                    if (ctx?.Enemies != null)
                        foreach (var enemy in ctx.Enemies)
                            yield return enemy;
                    break;
            }
        }

        bool MatchesRange(float distance, AuraOp op)
        {
            switch (op.RangeMode)
            {
                case AuraRangeMode.Within:
                    return distance <= op.Radius;
                case AuraRangeMode.Between:
                    return distance >= op.MinRadius && distance <= op.MaxRadius;
                case AuraRangeMode.Exact:
                    return Math.Abs(distance - op.Radius) < 0.01f;
                default:
                    return false;
            }
        }

        void ApplyAdditionalOps(AuraOp op, IReadOnlyList<Unit> targets, RuntimeCtx ctx)
        {
            if (op.AdditionalOperations == null || op.AdditionalOperations.Count == 0)
                return;

            var operations = new List<EffectOp>();
            foreach (var nested in op.AdditionalOperations)
            {
                switch (nested)
                {
                    case ApplyStatusOp statusOp:
                        operations.Add(CloneApplyStatus(statusOp, targets));
                        break;
                    case RemoveStatusOp removeOp:
                        operations.Add(CloneRemoveStatus(removeOp, targets));
                        break;
                    default:
                        operations.Add(nested);
                        break;
                }
            }

            EffectOpRunner.Run(operations, ctx);
        }

        ApplyStatusOp CloneApplyStatus(ApplyStatusOp op, IReadOnlyList<Unit> targets)
        {
            return new ApplyStatusOp
            {
                Source = op.Source,
                Targets = targets,
                TargetType = op.TargetType,
                StatusSkillId = op.StatusSkillId,
                DurationSeconds = op.DurationSeconds,
                IsPermanent = op.IsPermanent,
                IsInstant = op.IsInstant,
                StackCount = op.StackCount,
                MaxStacks = op.MaxStacks,
                Probability = op.Probability,
                Condition = op.Condition,
                Accumulator = op.Accumulator,
                InstantOperations = op.InstantOperations
            };
        }

        RemoveStatusOp CloneRemoveStatus(RemoveStatusOp op, IReadOnlyList<Unit> targets)
        {
            return new RemoveStatusOp
            {
                Targets = targets,
                TargetType = op.TargetType,
                StatusSkillIds = op.StatusSkillIds,
                RemovalMode = op.RemovalMode,
                StackCount = op.StackCount,
                ShowStacks = op.ShowStacks,
                MaxStacks = op.MaxStacks,
                Replacement = op.Replacement,
                Probability = op.Probability,
                Condition = op.Condition
            };
        }
    }
}
