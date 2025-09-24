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

            var destination = ComputeDestination(unit, op, ctx);
            if (destination == unit.Position)
                return;

            var from = unit.Position;
            unit.Position = destination;
            ctx.Grid?.SetPosition(unit, destination);
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

        HexCoord ComputeDestination(Unit unit, MoveOp op, RuntimeCtx ctx)
        {
            int distance = DetermineAllowedDistance(unit, op, ctx);

            var offset = ResolveOffset(unit, op, ctx, distance);
            if (offset == HexCoord.Zero)
                return unit.Position;

            var target = unit.Position + offset;
            var grid = ctx.Grid;
            if (grid?.Layout != null)
            {
                var layout = grid.Layout;
                if (!layout.Contains(target))
                {
                    if (!op.AllowPartialMove)
                        return unit.Position;
                    target = layout.ClampToBounds(unit.Position, target);
                }

                if (!op.ForceMovement && !op.IgnoreObstacles)
                {
                    var adjusted = FindLastFree(unit.Position, target, grid, op.AllowPartialMove);
                    if (adjusted == unit.Position)
                        return unit.Position;
                    target = adjusted;
                }
            }
            return target;
        }

        int DetermineAllowedDistance(Unit unit, MoveOp op, RuntimeCtx ctx)
        {
            int requested = Math.Max(0, op.Distance);
            float speed = unit?.Stats?.MoveSpeed ?? 0f;
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
                int targetDistance = HexCoord.Distance(unit.Position, ctx.PrimaryTarget.Position);
                if (targetDistance > 0)
                    distance = Math.Min(distance, Math.Max(0, targetDistance - 1));
            }

            return Math.Max(0, distance);
        }

        HexCoord ResolveOffset(Unit unit, MoveOp op, RuntimeCtx ctx, int distance)
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
                        var target = ctx.PrimaryTarget?.Position ?? unit.Position;
                        if (target == unit.Position)
                            return HexCoord.Zero;
                        var destination = HexCoord.MoveTowards(unit.Position, target, distance);
                        return destination - unit.Position;
                    }
                case MoveDirection.AwayFromTarget:
                    {
                        var anchor = ctx.PrimaryTarget?.Position ?? unit.Position;
                        if (anchor == unit.Position)
                            return HexCoord.Zero;
                        var mirrored = unit.Position + (unit.Position - anchor);
                        var destination = HexCoord.MoveTowards(unit.Position, mirrored, distance);
                        return destination - unit.Position;
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
