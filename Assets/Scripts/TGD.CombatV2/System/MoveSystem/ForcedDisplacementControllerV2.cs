using UnityEngine;
using TGD.CombatV2.Integration;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerOccupancyBridge))]
    public sealed class ForcedDisplacementControllerV2 : MonoBehaviour
    {
        [Header("Refs")]
        public HexOccupancyService occupancyService;
        public bool debugLog;

        PlayerOccupancyBridge _bridge;
        HexOccupancy _occ;

        void Awake()
        {
            _bridge = GetComponent<PlayerOccupancyBridge>();
            if (!occupancyService)
                occupancyService = GetComponent<HexOccupancyService>() ?? GetComponentInParent<HexOccupancyService>(true);
        }

        void OnEnable()
        {
            if (_bridge == null)
                _bridge = GetComponent<PlayerOccupancyBridge>();
            if (!occupancyService && _bridge != null && _bridge.occupancyService)
                occupancyService = _bridge.occupancyService;
        }

        void OnDisable()
        {
            _occ = null;
        }

        IGridActor SelfActor => _bridge?.Actor as IGridActor;

        public bool ApplyForcedDisplacement(Hex desiredAnchor, Facing4? facingOverride = null)
        {
            if (_bridge == null)
                return false;

            _bridge.EnsurePlacedNow();

            if (occupancyService)
                _occ = occupancyService.Get();
            else if (_bridge != null && _bridge.occupancyService)
                _occ = _bridge.occupancyService.Get();

            var actor = SelfActor;
            var finalFacing = facingOverride ?? (actor != null ? actor.Facing : Facing4.PlusQ);

            var destination = ResolveDestination(desiredAnchor, finalFacing, actor);

            if (actor != null && _occ != null && !_occ.CanPlace(actor, destination, finalFacing, actor))
            {
                if (debugLog)
                    Debug.LogWarning($"[ForceMove] Destination blocked {destination}", this);
                return false;
            }

            return _bridge.MoveCommit(destination, finalFacing);
        }

        Hex ResolveDestination(Hex desired, Facing4 facing, IGridActor actor)
        {
            if (_occ == null || actor == null)
                return desired;

            if (_occ.CanPlace(actor, desired, facing, actor))
                return desired;

            if (debugLog)
                Debug.LogWarning($"[ForceMove] ResolveDestination fallback to current anchor from {desired}", this);
            return _bridge != null ? _bridge.CurrentAnchor : desired;
        }
    }
}
