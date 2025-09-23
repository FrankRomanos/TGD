using System;
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
            if (op == null)
                return;

            var unit = ResolveSubject(op.Subject, ctx);
            if (unit == null)
                return;

            var destination = ComputeDestination(unit, op, ctx);
            if (destination == unit.Position)
                return;

            var from = unit.Position;
            unit.Position = destination;
            _bus?.EmitUnitPositionChanged(unit, from, destination);
            _logger?.Log("MOVE_COMMIT", unit.UnitId, from, destination);
        }

        Unit ResolveSubject(MoveSubject subject, RuntimeCtx ctx)
        {
            return subject switch
            {
                MoveSubject.Caster => ctx?.Caster,
                MoveSubject.PrimaryTarget => ctx?.PrimaryTarget,
                MoveSubject.SecondaryTarget => ctx?.PrimaryTarget, // fallback
                _ => ctx?.Caster
            };
        }

        Vector2Int ComputeDestination(Unit unit, MoveOp op, RuntimeCtx ctx)
        {
            var offset = ResolveOffset(unit, op, ctx);
            if (op.MaxDistance > 0 && offset.magnitude > op.MaxDistance)
                offset = ClampMagnitude(offset, op.MaxDistance);

            return unit.Position + offset;
        }

        Vector2Int ResolveOffset(Unit unit, MoveOp op, RuntimeCtx ctx)
        {
            if (op.Execution == MoveExecution.Teleport && op.Offset != Vector2Int.zero)
                return op.Offset;

            if (op.Direction == MoveDirection.AbsoluteOffset)
                return op.Offset;

            Vector2Int baseDirection = op.Direction switch
            {
                MoveDirection.Forward => new Vector2Int(0, 1),
                MoveDirection.Backward => new Vector2Int(0, -1),
                MoveDirection.Left => new Vector2Int(-1, 0),
                MoveDirection.Right => new Vector2Int(1, 0),
                MoveDirection.TowardTarget => DirectionTo(unit.Position, ctx?.PrimaryTarget?.Position ?? unit.Position),
                MoveDirection.AwayFromTarget => -DirectionTo(unit.Position, ctx?.PrimaryTarget?.Position ?? unit.Position),
                _ => Vector2Int.zero
            };

            int distance = op.Distance != 0 ? op.Distance : 1;
            if (!string.IsNullOrWhiteSpace(op.DistanceExpression) && ctx?.Time != null)
            {
                if (int.TryParse(op.DistanceExpression, out var parsed))
                    distance = parsed;
            }

            return baseDirection * distance;
        }

        Vector2Int ClampMagnitude(Vector2Int vector, int maxDistance)
        {
            if (vector == Vector2Int.zero)
                return vector;

            float magnitude = vector.magnitude;
            if (magnitude <= maxDistance)
                return vector;

            var ratio = maxDistance / magnitude;
            var scaled = new Vector2(vector.x * ratio, vector.y * ratio);
            return new Vector2Int(Mathf.RoundToInt(scaled.x), Mathf.RoundToInt(scaled.y));
        }

        Vector2Int DirectionTo(Vector2Int from, Vector2Int to)
        {
            var diff = to - from;
            if (diff == Vector2Int.zero)
                return Vector2Int.zero;

            diff.x = Math.Sign(diff.x);
            diff.y = Math.Sign(diff.y);
            return diff;
        }
    }
}
