using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2.Targeting
{
    [DisallowMultipleComponent]
    public sealed class DefaultTargetValidator : MonoBehaviour, ITargetValidator
    {
        [Header("Sources")]
        public HexBoardAuthoringLite authoring;
        public HexOccupancyService occupancyService;
        public HexEnvironmentSystem environment;
        public TurnManagerV2 turnManager;
        public SimpleEnemyRegistry enemyRegistry;

        [Header("Blocking")]
        public bool blockByUnits = true;
        public bool blockByPhysics = true;
        public LayerMask obstacleMask;
        [Range(0.1f, 2f)] public float physicsRadiusScale = 0.9f;
        [Range(0.1f, 5f)] public float physicsProbeHeight = 2f;
        public bool includeTriggerColliders;
        public float physicsYOffset = 0.01f;

        readonly List<HexBoardTestDriver> _drivers = new();
        HexOccupancy _cachedOccupancy;

        public void SetDrivers(IEnumerable<HexBoardTestDriver> drivers)
        {
            _drivers.Clear();
            if (drivers == null) return;
            foreach (var driver in drivers)
            {
                if (driver == null) continue;
                if (_drivers.Contains(driver)) continue;
                _drivers.Add(driver);
            }
        }

        public TargetCheckResult Check(Unit actor, Hex hex, TargetingSpec spec)
        {
            var occType = DetectOccupant(actor, hex, out var unitOnHex);
            string occLabel = OccupantLabel(occType);

            bool hasUnit = occType != TargetOccupantMask.Empty;

            if (hasUnit && spec.requireEmpty)
                return Reject(hex, occLabel, spec, TargetInvalidReason.EmptyNotAllowed, unitOnHex);
            if (!hasUnit && spec.requireOccupied)
                return Reject(hex, occLabel, spec, TargetInvalidReason.EmptyNotAllowed, unitOnHex);

            if (occType == TargetOccupantMask.Self && !spec.allowSelf)
                return Reject(hex, occLabel, spec, TargetInvalidReason.Self, unitOnHex);

            if (!IsOccupantAllowed(spec, occType))
            {
                var reason = occType switch
                {
                    TargetOccupantMask.Self => TargetInvalidReason.Self,
                    TargetOccupantMask.Ally => TargetInvalidReason.Friendly,
                    TargetOccupantMask.Enemy => TargetInvalidReason.EnemyNotAllowed,
                    _ => TargetInvalidReason.EmptyNotAllowed
                };
                return Reject(hex, occLabel, spec, reason, unitOnHex);
            }

            if (RequiresNonObstacle(spec) && IsTerrainBlocked(actor, hex))
                return Reject(hex, occLabel, spec, TargetInvalidReason.Blocked, unitOnHex);

            var result = new TargetCheckResult
            {
                ok = true,
                reason = TargetInvalidReason.None,
                hitUnit = unitOnHex
            };
            LogPass(hex, occLabel, spec);
            return result;
        }

        TargetCheckResult Reject(Hex hex, string occLabel, TargetingSpec spec, TargetInvalidReason reason, Unit hit)
        {
            var result = new TargetCheckResult
            {
                ok = false,
                reason = reason,
                hitUnit = hit
            };
            LogReject(hex, occLabel, reason, spec);
            return result;
        }

        TargetOccupantMask DetectOccupant(Unit actor, Hex hex, out Unit unit)
        {
            unit = null;
            if (actor != null && actor.Position.Equals(hex))
            {
                unit = actor;
                return TargetOccupantMask.Self;
            }

            foreach (var driver in _drivers)
            {
                var map = driver?.Map;
                if (map != null && map.TryGetAt(hex, out var mappedUnit) && mappedUnit != null)
                {
                    unit = mappedUnit;
                    return ClassifyUnit(actor, mappedUnit);
                }
            }

            if (enemyRegistry != null && enemyRegistry.IsEnemy(hex))
                return TargetOccupantMask.Enemy;

            return TargetOccupantMask.Empty;
        }

        TargetOccupantMask ClassifyUnit(Unit actor, Unit occupant)
        {
            if (occupant == null)
                return TargetOccupantMask.Empty;
            if (actor != null && ReferenceEquals(actor, occupant))
                return TargetOccupantMask.Self;

            if (turnManager != null)
            {
                bool occEnemy = turnManager.IsEnemyUnit(occupant);
                bool occPlayer = turnManager.IsPlayerUnit(occupant);
                if (actor != null)
                {
                    bool actorEnemy = turnManager.IsEnemyUnit(actor);
                    bool actorPlayer = turnManager.IsPlayerUnit(actor);
                    if (actorPlayer)
                        return occPlayer ? TargetOccupantMask.Ally : TargetOccupantMask.Enemy;
                    if (actorEnemy)
                        return occEnemy ? TargetOccupantMask.Ally : TargetOccupantMask.Enemy;
                }

                if (occEnemy)
                    return TargetOccupantMask.Enemy;
                if (occPlayer)
                    return TargetOccupantMask.Ally;
            }

            if (enemyRegistry != null && enemyRegistry.IsEnemy(occupant.Position))
                return TargetOccupantMask.Enemy;

            return TargetOccupantMask.Enemy;
        }

        bool IsOccupantAllowed(TargetingSpec spec, TargetOccupantMask occType)
        {
            if (occType == TargetOccupantMask.Empty)
                return (spec.occupant & TargetOccupantMask.Empty) != 0;
            return (spec.occupant & occType) != 0;
        }

        bool RequiresNonObstacle(TargetingSpec spec)
            => (spec.terrain & TargetTerrainMask.NonObstacle) != 0 || spec.terrain == 0;

        bool IsTerrainBlocked(Unit actor, Hex hex)
        {
            var layout = authoring?.Layout;
            if (layout != null && !layout.Contains(hex))
                return true;

            if (actor != null && actor.Position.Equals(hex))
                return false;

            if (environment != null && environment.IsPit(hex))
                return true;

            if (blockByUnits)
            {
                var occ = ResolveOccupancy();
                if (occ != null && occ.IsBlocked(hex))
                    return true;
            }

            if (blockByPhysics && obstacleMask.value != 0 && layout != null)
            {
                if (CheckPhysics(layout, hex))
                    return true;
            }

            return false;
        }

        HexOccupancy ResolveOccupancy()
        {
            if (_cachedOccupancy != null)
                return _cachedOccupancy;
            if (occupancyService != null)
                _cachedOccupancy = occupancyService.Get();
            return _cachedOccupancy;
        }

        bool CheckPhysics(HexBoardLayout layout, Hex hex)
        {
            Vector3 center = layout.World(hex, physicsYOffset);
            const float sqrt3Over2 = 0.8660254f;
            float radius = (authoring != null ? authoring.cellSize * sqrt3Over2 : 0.5f) * physicsRadiusScale;
            radius = Mathf.Max(0.01f, radius);
            var triggerMode = includeTriggerColliders ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;

            Vector3 sphereCenter = center + Vector3.up * 0.5f;
            if (Physics.CheckSphere(sphereCenter, radius, obstacleMask, triggerMode))
                return true;

            Vector3 p1 = center + Vector3.up * 0.1f;
            Vector3 p2 = center + Vector3.up * Mathf.Max(0.1f, physicsProbeHeight);
            return Physics.CheckCapsule(p1, p2, radius, obstacleMask, triggerMode);
        }

        void LogPass(Hex hex, string occLabel, TargetingSpec spec)
        {
            Debug.Log($"[Probe] hex=({hex.q},{hex.r}) occ={occLabel} pass plan={PlanLabel(spec)} (spec={spec})", this);
        }

        void LogReject(Hex hex, string occLabel, TargetInvalidReason reason, TargetingSpec spec)
        {
            Debug.Log($"[Probe] hex=({hex.q},{hex.r}) occ={occLabel} reject reason={reason} (spec={spec})", this);
        }

        static string PlanLabel(TargetingSpec spec)
        {
            if (spec.requireOccupied && (spec.occupant & TargetOccupantMask.Enemy) != 0)
                return "Move+Attack";
            if (spec.requireEmpty || (spec.occupant & TargetOccupantMask.Empty) != 0)
                return "MoveOnly";
            return "Unknown";
        }

        static string OccupantLabel(TargetOccupantMask mask)
        {
            return mask switch
            {
                TargetOccupantMask.Empty => "None",
                TargetOccupantMask.Self => "Self",
                TargetOccupantMask.Ally => "Ally",
                TargetOccupantMask.Enemy => "Enemy",
                _ => mask.ToString()
            };
        }
    }
}
