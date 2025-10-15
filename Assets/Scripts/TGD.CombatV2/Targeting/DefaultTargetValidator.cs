using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2.Targeting
{
    [DisallowMultipleComponent]
    public sealed class DefaultTargetValidator : MonoBehaviour, ITargetValidator
    {
        [Tooltip("正式占位层（只读）")]
        public HexOccupancyService occupancyService;

        [Tooltip("简单敌人注册表（只读）")]
        public TGD.CombatV2.SimpleEnemyRegistry enemyRegistry;

        [Tooltip("调试输出")]
        public bool debugLog;

        HexOccupancy _occ;

        void Awake()
        {
            _occ = occupancyService ? occupancyService.Get() : null;
        }

        public TargetCheckResult Check(Unit actor, Hex hex, TargetingSpec spec)
        {
            if (_occ == null)
            {
                return Reject(TargetInvalidReason.Unknown, "[Probe] NoOccupancy", actor, hex, spec, null, HitKind.None);
            }

            if (spec == null)
            {
                return Reject(TargetInvalidReason.Unknown, "[Probe] NoSpec", actor, hex, spec, null, HitKind.None);
            }

            if (spec.terrain == TargetTerrainMask.NonObstacle && _occ.IsObstacle(hex))
            {
                return Reject(TargetInvalidReason.Blocked, "[Probe] Terrain=Obstacle", actor, hex, spec, null, HitKind.None);
            }

            _occ.TryGetActor(hex, out var rawActor);
            var unitAt = rawActor is UnitGridAdapter adapter ? adapter.Unit : null;
            bool enemyMarked = enemyRegistry != null && enemyRegistry.IsEnemy(hex);
            bool isEmpty = rawActor == null && !enemyMarked;
            var hit = ClassifyHit(actor, unitAt, hex, rawActor, enemyMarked);

            if (hit == HitKind.Self && !spec.allowSelf)
            {
                return Reject(TargetInvalidReason.Self, "[Probe] SelfNotAllowed", actor, hex, spec, unitAt, hit);
            }

            bool allowEmpty = (spec.occupant & TargetOccupantMask.Empty) != 0;
            bool allowEnemy = (spec.occupant & TargetOccupantMask.Enemy) != 0;
            bool allowAlly = (spec.occupant & TargetOccupantMask.Ally) != 0;
            bool allowSelfMask = (spec.occupant & TargetOccupantMask.Self) != 0;

            if (isEmpty && !allowEmpty)
            {
                return Reject(TargetInvalidReason.EmptyNotAllowed, "[Probe] EmptyNotAllowed", actor, hex, spec, unitAt, hit);
            }

            if (hit == HitKind.Enemy && !allowEnemy)
            {
                return Reject(TargetInvalidReason.EnemyNotAllowed, "[Probe] EnemyNotAllowed", actor, hex, spec, unitAt, hit);
            }

            if (hit == HitKind.Ally && !allowAlly)
            {
                return Reject(TargetInvalidReason.Friendly, "[Probe] AllyNotAllowed", actor, hex, spec, unitAt, hit);
            }

            if (hit == HitKind.Self && !allowSelfMask && !spec.allowSelf)
            {
                return Reject(TargetInvalidReason.Self, "[Probe] SelfMaskNotAllowed", actor, hex, spec, unitAt, hit);
            }

            if (spec.requireOccupied && isEmpty)
            {
                return Reject(TargetInvalidReason.EmptyNotAllowed, "[Probe] RequireOccupied", actor, hex, spec, unitAt, hit);
            }

            if (spec.requireEmpty && !isEmpty)
            {
                return Reject(TargetInvalidReason.Blocked, "[Probe] RequireEmpty", actor, hex, spec, unitAt, hit);
            }

            if (spec.maxRangeHexes >= 0 && actor != null)
            {
                var anchor = actor.Position;
                int dist = Hex.Distance(anchor, hex);
                if (dist > spec.maxRangeHexes)
                {
                    return Reject(TargetInvalidReason.OutOfRange, $"[Probe] OutOfRange({dist}>{spec.maxRangeHexes})", actor, hex, spec, unitAt, hit);
                }
            }

            var plan = DerivePlan(hit);
            bool ok = plan != PlanKind.None;
            var res = new TargetCheckResult
            {
                ok = ok,
                reason = ok ? TargetInvalidReason.None : TargetInvalidReason.Unknown,
                hitUnit = unitAt,
                hit = hit,
                plan = plan
            };

            if (debugLog)
            {
                Debug.Log($"[Probe] {spec} @ {hex} → {res}", this);
            }

            return res;
        }

        TargetCheckResult Reject(TargetInvalidReason reason, string why, Unit actor, Hex hex, TargetingSpec spec, Unit unitAt, HitKind hit)
        {
            var res = new TargetCheckResult
            {
                ok = false,
                reason = reason,
                hitUnit = unitAt,
                hit = hit,
                plan = PlanKind.None
            };

            if (debugLog)
            {
                Debug.Log($"{why} {spec} @ {hex} → {reason}", this);
            }

            return res;
        }

        HitKind ClassifyHit(Unit actor, Unit unitAt, Hex hex, IGridActor rawActor, bool enemyMarked)
        {
            if (actor != null && unitAt != null && ReferenceEquals(actor, unitAt))
                return HitKind.Self;

            if (enemyMarked)
                return HitKind.Enemy;

            if (rawActor == null)
                return HitKind.None;

            if (unitAt != null)
                return HitKind.Ally;

            return HitKind.Ally;
        }

        static PlanKind DerivePlan(HitKind hit)
        {
            switch (hit)
            {
                case HitKind.Enemy:
                    return PlanKind.MoveAndAttack;
                case HitKind.None:
                    return PlanKind.MoveOnly;
                case HitKind.Self:
                    return PlanKind.AttackOnly;
                default:
                    return PlanKind.None;
            }
        }
    }

    static class OccupancyExtensions
    {
        public static bool IsObstacle(this HexOccupancy occ, Hex hex)
        {
            if (occ == null) return true;
            return occ.IsBlockedFormal(hex);
        }

        public static Unit TryGetUnit(this HexOccupancy occ, Hex hex)
        {
            if (occ == null) return null;
            if (!occ.TryGetActor(hex, out var actor)) return null;
            if (actor is UnitGridAdapter unitAdapter)
                return unitAdapter.Unit;
            return null;
        }

        public static Hex GetAnchor(this HexOccupancy occ, Unit unit)
        {
            if (unit == null)
                return Hex.Zero;
            return unit.Position;
        }
    }
}
