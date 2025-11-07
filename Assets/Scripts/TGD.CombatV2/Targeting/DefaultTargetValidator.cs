using UnityEngine;
using TGD.HexBoard;
using TGD.HexBoard.Path;
using TGD.CoreV2;
using TGD.CombatV2;

namespace TGD.CombatV2.Targeting
{
    [DisallowMultipleComponent]
    public sealed class DefaultTargetValidator : MonoBehaviour, ITargetValidator
    {
        [Tooltip("正式占位层（只读）")]
        public HexOccupancyService occupancyService;

        [Tooltip("调试输出")]
        public bool debugLog;

        [Tooltip("环境阻挡查询（Pit 等）")]
        public HexEnvironmentSystem environment;

        [Tooltip("TurnManagerV2 roster (只读)")]
        public TurnManagerV2 turnManager;

        HexOccupancy _occ;

        void Awake()
        {
            AutoWire();
            RefreshOccupancy();
        }

        void AutoWire()
        {
            if (!occupancyService)
                occupancyService = GetComponentInParent<HexOccupancyService>(true);

            if (!environment)
            {
                environment = GetComponentInParent<HexEnvironmentSystem>(true);
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

            if (!turnManager)
                turnManager = GetComponentInParent<TurnManagerV2>(true);
        }

        public void InjectServices(TurnManagerV2 tm, HexOccupancyService occSvc, HexEnvironmentSystem env = null)
        {
            if (occSvc != null)
                occupancyService = occSvc;
            if (tm != null)
                turnManager = tm;
            if (env != null)
                environment = env;
            RefreshOccupancy();
        }

        void RefreshOccupancy()
        {
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
                RefreshOccupancy();

            if (_occ == null)
                return RejectEarly(TargetInvalidReason.Unknown, "[Probe] NoOccupancyService");

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
            else if (allowsGround && !IsAnyClick(spec))
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

            HitKind hit = ClassifyHit(actor, actorAt, unitAt);

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

            var plan = ResolvePlan(hit);

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

        HitKind ClassifyHit(Unit self, IGridActor actorAt, Unit unitAt)
        {
            if (actorAt == null)
                return HitKind.None;

            if (unitAt != null)
            {
                if (self != null && ReferenceEquals(unitAt, self))
                    return HitKind.Self;

                if (IsEnemy(self, unitAt))
                    return HitKind.Enemy;

                if (IsAlly(self, unitAt))
                    return HitKind.Ally;

                // 未知阵营：暂时视作敌方，避免卡死
                return HitKind.Enemy;
            }

            // 未能映射到 Unit：临时视作敌人，等待完整链路
            return HitKind.Enemy;
        }

        PlanKind ResolvePlan(HitKind hit)
        {
            switch (hit)
            {
                case HitKind.Enemy:
                    return PlanKind.MoveAndAttack;
                case HitKind.None:
                    return PlanKind.MoveOnly;
                case HitKind.Self:
                    return PlanKind.AttackOnly;
                case HitKind.Ally:
                    return PlanKind.None;
                default:
                    return PlanKind.None;
            }
        }

        bool IsEnemy(Unit self, Unit other)
        {
            if (self == null || other == null)
                return false;

            if (ReferenceEquals(self, other))
                return false;

            if (turnManager == null)
                return false;

            bool selfPlayer = turnManager.IsPlayerUnit(self);
            bool selfEnemy = turnManager.IsEnemyUnit(self);

            if (selfPlayer)
                return turnManager.IsEnemyUnit(other);

            if (selfEnemy)
                return turnManager.IsPlayerUnit(other);

            // 自己没登记：按对方阵营兜底
            if (turnManager.IsEnemyUnit(other))
                return true;

            return false;
        }

        bool IsAlly(Unit self, Unit other)
        {
            if (self == null || other == null)
                return false;

            if (ReferenceEquals(self, other))
                return true;

            if (turnManager == null)
                return false;

            bool selfPlayer = turnManager.IsPlayerUnit(self);
            bool selfEnemy = turnManager.IsEnemyUnit(self);

            if (selfPlayer)
                return turnManager.IsPlayerUnit(other);

            if (selfEnemy)
                return turnManager.IsEnemyUnit(other);

            if (!turnManager.IsEnemyUnit(other) && turnManager.IsPlayerUnit(other))
                return true;

            return false;
        }
    }
}
