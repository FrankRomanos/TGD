using UnityEngine;
using TGD.HexBoard;
using TGD.HexBoard.Path;

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
            if (!occupancyService)
                occupancyService = GetComponentInParent<HexBoardTestDriver>(true)?.occupancyService;
            _occ = occupancyService ? occupancyService.Get() : null;
        }

        public TargetCheckResult Check(Unit actor, Hex hex, TargetingSpec spec)
        {
            TargetCheckResult RejectEarly(TargetInvalidReason reason, string why)
            {
                var rej = new TargetCheckResult
                {
                    ok = false,
                    reason = reason,
                    hitUnit = null,
                    hit = HitKind.None,
                    plan = PlanKind.None
                };

                if (debugLog)
                {
                    var specLabel = spec != null ? spec.ToString() : "<null>";
                    Debug.Log($"{why} {specLabel} @ {hex} → {reason}", this);
                }

                return rej;
            }

            if (spec == null)
                return RejectEarly(TargetInvalidReason.Unknown, "[Probe] NoSpec");

            if (_occ == null)
                return RejectEarly(TargetInvalidReason.Unknown, "[Probe] NoOccupancyService");

            if (spec.terrain == TargetTerrainMask.NonObstacle)
            {
                var terrainPass = PassabilityFactory.StaticTerrainOnly(_occ);
                if (terrainPass != null && terrainPass.IsBlocked(hex))
                    return RejectEarly(TargetInvalidReason.Blocked, "[Probe] Terrain=StaticObstacle");
            }

            _occ.TryGetActor(hex, out var actorAt);
            var unitAt = ResolveUnit(actorAt);
            bool isEmpty = actorAt == null;
            bool enemyMarked = enemyRegistry != null && enemyRegistry.IsEnemyAt(hex, _occ);

            HitKind hit;
            if (isEmpty && !enemyMarked)
            {
                hit = HitKind.None;
            }
            else if (unitAt != null && actor != null && ReferenceEquals(unitAt, actor))
            {
                hit = HitKind.Self;
            }
            else if (enemyMarked || (actorAt != null && enemyRegistry != null && enemyRegistry.IsEnemyActor(actorAt)))
            {
                hit = HitKind.Enemy;
            }
            else
            {
                hit = HitKind.Ally;
            }

            bool allowEmpty = (spec.occupant & TargetOccupantMask.Empty) != 0;
            bool allowEnemy = (spec.occupant & TargetOccupantMask.Enemy) != 0;
            bool allowAlly = (spec.occupant & TargetOccupantMask.Ally) != 0;
            bool allowSelfMask = (spec.occupant & TargetOccupantMask.Self) != 0;

            TargetCheckResult Reject(TargetInvalidReason reason, string why)
            {
                var rej = new TargetCheckResult
                {
                    ok = false,
                    reason = reason,
                    hitUnit = unitAt,
                    hit = hit,
                    plan = PlanKind.None
                };

                if (debugLog)
                    Debug.Log($"{why} {spec} @ {hex} → {reason}", this);

                return rej;
            }

            if (hit == HitKind.Self && !(spec.allowSelf || allowSelfMask))
                return Reject(TargetInvalidReason.Self, "[Probe] SelfNotAllowed");

            if (isEmpty && !allowEmpty)
                return Reject(TargetInvalidReason.EmptyNotAllowed, "[Probe] EmptyNotAllowed");

            if (hit == HitKind.Enemy && !allowEnemy)
                return Reject(TargetInvalidReason.EnemyNotAllowed, "[Probe] EnemyNotAllowed");

            if (hit == HitKind.Ally && !allowAlly)
                return Reject(TargetInvalidReason.Friendly, "[Probe] AllyNotAllowed");

            if (spec.requireOccupied && (isEmpty && !enemyMarked))
                return Reject(TargetInvalidReason.EmptyNotAllowed, "[Probe] RequireOccupied");

            if (spec.requireEmpty && (!isEmpty || enemyMarked))
                return Reject(TargetInvalidReason.Blocked, "[Probe] RequireEmpty");

            if (spec.maxRangeHexes >= 0 && actor != null)
            {
                var anchor = actor.Position;
                if (_occ.TryGetAnchor(actor, out var anchorHex))
                    anchor = anchorHex;
                int dist = Hex.Distance(anchor, hex);
                if (dist > spec.maxRangeHexes)
                    return Reject(TargetInvalidReason.OutOfRange, $"[Probe] OutOfRange({dist}>{spec.maxRangeHexes})");
            }

            var plan = hit switch
            {
                HitKind.Enemy => PlanKind.MoveAndAttack,
                HitKind.None => PlanKind.MoveOnly,
                HitKind.Self => PlanKind.AttackOnly,
                _ => PlanKind.None
            };

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
                Debug.Log($"[Probe] {spec} @ {hex} → {res}", this);

            return res;
        }

        static Unit ResolveUnit(IGridActor actor)
        {
            if (actor is Unit unit)
                return unit;
            if (actor is UnitGridAdapter adapter)
                return adapter.Unit;
            return null;
        }
    }
}
