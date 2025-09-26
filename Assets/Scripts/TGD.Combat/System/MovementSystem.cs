using System;
using TGD.Data;
using TGD.Grid;
using UnityEngine;


namespace TGD.Combat
{
    public sealed class MovementSystem : IMovementSystem
    {
        readonly ICombatEventBus _bus;
        readonly ICombatLogger _logger;

        public MovementSystem(ICombatEventBus bus, ICombatLogger logger)
        {
            _bus = bus;
            _logger = logger;
        }

        public void Execute(MoveOp op, RuntimeCtx ctx)
        {
            if (op == null || ctx == null)
                return;

            var unit = ResolveSubject(op.Subject, ctx);
            if (unit == null)
                return;

            var grid = ctx.Grid;
            var current = CombatGridUtility.Resolve(unit, grid);
            var destination = ComputeDestination(unit, current, op, ctx);
            if (destination == current)
                return;

            var from = current;
            if (grid != null)
                grid.SetPosition(unit, destination);

            unit.Position = destination;

            _bus?.EmitUnitPositionChanged(unit, from, destination);
            _logger?.Log("MOVE_COMMIT", unit.UnitId, from, destination);
        }

        Unit ResolveSubject(MoveSubject subject, RuntimeCtx ctx)
        {
            return subject switch
            {
                MoveSubject.Caster => ctx.Caster,
                MoveSubject.PrimaryTarget => ctx.PrimaryTarget,
                MoveSubject.SecondaryTarget => ctx.SecondaryTarget,
                _ => ctx.Caster
            };
        }

        HexCoord ComputeDestination(Unit unit, HexCoord current, MoveOp op, RuntimeCtx ctx)
        {
            int distance = DetermineAllowedDistance(unit, current, op, ctx);

            var offset = ResolveOffset(unit, current, op, ctx, distance);
            if (offset == HexCoord.Zero)
                return current;

            var target = current + offset;
            var grid = ctx.Grid;
            if (grid?.Layout != null)
            {
                var layout = grid.Layout;
                if (!layout.Contains(target))
                {
                    if (!op.AllowPartialMove)
                        return current;
                    target = layout.ClampToBounds(current, target);
                }

                if (!op.ForceMovement && !op.IgnoreObstacles)
                {
                    var adjusted = FindLastFree(current, target, grid, op.AllowPartialMove);
                    if (adjusted == current)
                        return current;
                    target = adjusted;
                }
            }
            return target;
        }

        int DetermineAllowedDistance(Unit unit, HexCoord current, MoveOp op, RuntimeCtx ctx)
        {
            int requested = Math.Max(0, op.Distance);
            float speed = 0f;
            if (unit?.Stats != null)
            {
                speed = unit.Stats.Movement;
                if (Mathf.Approximately(speed, 0f))
                    speed = unit.Stats.MoveSpeed;
            }
            float duration = op.DurationSeconds > 0f ? op.DurationSeconds : ctx.Skill?.timeCostSeconds ?? 0f;
            if (duration <= 0f)
                duration = 1f;

            int speedCap = 0;
            if (speed > 0f)
            {
                float raw = speed * duration;
                speedCap = Mathf.FloorToInt(raw);
                if (op.AllowPartialMove && speedCap == 0 && raw > 0f)
                    speedCap = 1;
            }

            int distance = requested > 0 ? requested : speedCap;
            if (distance <= 0 && op.AllowPartialMove && speedCap > 0)
                distance = speedCap;

            if (speedCap > 0)
            {
                if (requested > 0 && !op.AllowPartialMove && speedCap < requested)
                    return 0;
                distance = Math.Min(distance, speedCap);
            }

            if (op.MaxDistance > 0)
                distance = Math.Min(distance, op.MaxDistance);

            if (op.StopAdjacentToTarget && ctx.PrimaryTarget != null)
            {
                var targetCoord = CombatGridUtility.Resolve(ctx.PrimaryTarget, ctx.Grid);
                int targetDistance = HexCoord.Distance(current, targetCoord);
                if (targetDistance > 0)
                    distance = Math.Min(distance, Math.Max(0, targetDistance - 1));
            }

            return Math.Max(0, distance);
        }

        HexCoord ResolveOffset(Unit unit, HexCoord current, MoveOp op, RuntimeCtx ctx, int distance)
        {
            if (op.Execution == MoveExecution.Teleport && op.Offset != HexCoord.Zero)
                return op.Offset;

            if (distance <= 0)
                return HexCoord.Zero;

            if (op.Direction == MoveDirection.AbsoluteOffset)
            {
                var offset = op.Offset;
                int length = offset.Length;
                if (length > distance)
                {
                    if (!op.AllowPartialMove)
                        return HexCoord.Zero;
                    offset = HexCoord.MoveTowards(HexCoord.Zero, offset, distance);
                }
                return offset;
            }
            switch (op.Direction)
            {
                case MoveDirection.TowardTarget:
                    {
                        var target = CombatGridUtility.Resolve(ctx.PrimaryTarget, ctx.Grid);
                        if (target == current)
                            return HexCoord.Zero;
                        var destination = HexCoord.MoveTowards(current, target, distance);
                        return destination - current;
                    }
                case MoveDirection.AwayFromTarget:
                    {
                        var anchor = CombatGridUtility.Resolve(ctx.PrimaryTarget, ctx.Grid);
                        if (anchor == current)
                            return HexCoord.Zero;
                        var mirrored = current + (current - anchor);
                        var destination = HexCoord.MoveTowards(current, mirrored, distance);
                        return destination - current;
                    }
                case MoveDirection.Forward:
                    return HexCoord.Directions[5] * distance;
                case MoveDirection.Backward:
                    return HexCoord.Directions[2] * distance;
                case MoveDirection.Left:
                    return HexCoord.Directions[3] * distance;
                case MoveDirection.Right:
                    return HexCoord.Directions[0] * distance;
                default:
                    return HexCoord.Zero;
            }
        }

        HexCoord FindLastFree(HexCoord start, HexCoord target, HexGridMap<Unit> grid, bool allowPartial)
        {
            if (grid == null)
                return target;

            HexCoord lastValid = start;
            bool blocked = false;
            foreach (var step in HexCoord.GetLine(start, target))
            {
                if (step == start)
                {
                    continue;
                }

                if (!grid.Layout.Contains(step))
                {
                    blocked = true;
                    break;
                }

                if (grid.HasAny(step))
                {
                    blocked = true;
                    break;
                }

                lastValid = step;
            }

            if (!blocked)
                return target;

            return allowPartial ? lastValid : start;
        }
    }
}
