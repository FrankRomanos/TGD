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

        [Tooltip("环境阻挡查询（Pit 等）")]
        public HexEnvironmentSystem environment;

        HexOccupancy _occ;

        void Awake()
        {
            var driver = GetComponentInParent<HexBoardTestDriver>(true);

            if (!occupancyService)
            {
                if (driver != null)
                {
                    if (driver.authoring != null)
                    {
                        occupancyService = driver.authoring.GetComponent<HexOccupancyService>();
                        if (!occupancyService)
                            occupancyService = driver.authoring.GetComponentInParent<HexOccupancyService>(true);
                    }

                    if (!occupancyService)
                        occupancyService = driver.GetComponentInParent<HexOccupancyService>(true);
                }
            }

            if (!occupancyService)
                occupancyService = GetComponentInParent<HexOccupancyService>(true);

            _occ = occupancyService ? occupancyService.Get() : null;

            if (!environment)
            {
                environment = GetComponentInParent<HexEnvironmentSystem>(true);
                if (!environment && driver != null)
                    environment = driver.GetComponentInParent<HexEnvironmentSystem>(true);
            }

            if (!environment && occupancyService != null)
            {
                environment = occupancyService.GetComponent<HexEnvironmentSystem>();
                if (!environment && occupancyService.authoring != null)
                {
                    environment = occupancyService.authoring.GetComponent<HexEnvironmentSystem>()
                        ?? occupancyService.authoring.GetComponentInParent<HexEnvironmentSystem>(true);
                }
            }
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

            if (_occ == null && occupancyService)
                _occ = occupancyService.Get();

            if (_occ == null)
                return RejectEarly(TargetInvalidReason.Unknown, "[Probe] NoOccupancyService");

            bool ignoreEnvironment = IsAnyClick(spec);
            bool allowsGround = AllowsGroundSelection(spec);
            // 1) 非障碍：遇到静态障碍或坑，立刻拒绝（早退）
            if (spec.terrain == TargetTerrainMask.NonObstacle)
            {
                var terrainPass = PassabilityFactory.StaticTerrainOnly(_occ);
                if (terrainPass != null && terrainPass.IsBlocked(hex))
                    return RejectEarly(TargetInvalidReason.Blocked, "[Probe] Terrain=StaticObstacle");

                if (environment != null && environment.IsPit(hex))
                    return RejectEarly(TargetInvalidReason.Blocked, "[Probe] Terrain=Pit");
            }
            // 2) 允许点地面但不是 AnyClick：同样早退拦住
            else if (AllowsGroundSelection(spec) && !IsAnyClick(spec))
            {
                var terrainPass = PassabilityFactory.StaticTerrainOnly(_occ);
                if (terrainPass != null && terrainPass.IsBlocked(hex))
                    return RejectEarly(TargetInvalidReason.Blocked, "[Probe] GroundStaticBlocked");

                if (environment != null && environment.IsPit(hex))
                    return RejectEarly(TargetInvalidReason.Blocked, "[Probe] GroundPit");
            }
            _occ.TryGetActor(hex, out var actorAt);
            var unitAt = ResolveUnit(actorAt);
            bool isEmpty = actorAt == null;
            bool enemyMarked = enemyRegistry != null && enemyRegistry.IsEnemyAt(hex, _occ);

            HitKind hit = HitKind.None;
            if (!isEmpty)
            {
                if (unitAt != null && actor != null && ReferenceEquals(unitAt, actor))
                {
                    hit = HitKind.Self;
                }
                else if (enemyRegistry != null && enemyRegistry.IsEnemyActor(actorAt))
                {
                    hit = HitKind.Enemy;
                }
                else
                {
                    hit = HitKind.Ally;
                }
            }
            else if (enemyMarked)
            {
                hit = HitKind.Enemy;
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

            if (isEmpty && hit == HitKind.None && !allowEmpty)
                return Reject(TargetInvalidReason.EmptyNotAllowed, "[Probe] EmptyNotAllowed");

            if (hit == HitKind.Enemy && !allowEnemy)
                return Reject(TargetInvalidReason.EnemyNotAllowed, "[Probe] EnemyNotAllowed");

            if (hit == HitKind.Ally && !allowAlly)
                return Reject(TargetInvalidReason.Friendly, "[Probe] AllyNotAllowed");

            if (spec.requireOccupied && (isEmpty && hit == HitKind.None))
                return Reject(TargetInvalidReason.EmptyNotAllowed, "[Probe] RequireOccupied");

            if (spec.requireEmpty && (hit != HitKind.None))
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
        static bool AllowsGroundSelection(TargetingSpec spec)
        {
            if (spec == null)
                return false;

            bool allowsEmpty = (spec.occupant & TargetOccupantMask.Empty) != 0;
            bool requiresOccupied = spec.requireOccupied;

            return allowsEmpty && !requiresOccupied;
        }

        static bool IsAnyClick(TargetingSpec spec)
        {
            if (spec == null)
                return false;

            return spec.terrain == TargetTerrainMask.Any
                && spec.occupant == TargetOccupantMask.Any
                && spec.allowSelf
                && !spec.requireEmpty
                && !spec.requireOccupied;
        }
        static Unit ResolveUnit(IGridActor actor)
        {
            if (actor is UnitGridAdapter adapter)
                return adapter.Unit;
            return null;
        }
    }
}
