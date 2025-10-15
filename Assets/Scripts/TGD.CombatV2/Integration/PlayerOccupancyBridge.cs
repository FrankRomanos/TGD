using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2.Integration
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HexBoardTestDriver))]
    public sealed class PlayerOccupancyBridge : MonoBehaviour, IActorOccupancyBridge
    {
        public HexOccupancyService occupancyService;
        public FootprintShape overrideFootprint;
        public bool debugLog;

        HexBoardTestDriver _driver;
        HexOccupancy _occ;
        IGridActor _actor;
        bool _placed;

        void Awake()
        {
            _driver = GetComponent<HexBoardTestDriver>();
            if (!occupancyService)
                occupancyService = GetComponent<HexOccupancyService>();
            if (!occupancyService && _driver != null)
            {
                occupancyService = _driver.GetComponentInParent<HexOccupancyService>(true);
                if (!occupancyService && _driver.authoring != null)
                {
                    occupancyService = _driver.authoring.GetComponent<HexOccupancyService>();
                    if (!occupancyService)
                        occupancyService = _driver.authoring.GetComponentInParent<HexOccupancyService>(true);
                }
            }

            _occ = occupancyService ? occupancyService.Get() : null;
            if (_occ == null && _driver != null && _driver.authoring?.Layout != null)
                _occ = new HexOccupancy(_driver.authoring.Layout);

            _actor = CreateActorAdapter();
        }

        void Start() => EnsurePlacedNow();

        void Update()
        {
            if (!_placed)
                EnsurePlacedNow();

#if UNITY_EDITOR
            if (_driver != null && _driver.IsReady && _placed)
            {
                var anchor = _actor.Anchor;
                if (!_driver.UnitRef.Position.Equals(anchor))
                {
                    Debug.LogWarning($"[Occ] Drift detected: driver={_driver.UnitRef.Position} occ={anchor}. Auto-mirror.", this);
                    MirrorDriver(anchor, _actor.Facing);
                }
            }
#endif
        }

        void OnDisable()
        {
            if (_occ != null && _actor != null && _placed)
            {
                _occ.Remove(_actor);
                _placed = false;
                if (debugLog)
                    Debug.Log($"[Occ] Remove {IdLabel()}", this);
            }
        }

        public bool IsReady => _driver && _driver.IsReady && _occ != null && _actor != null;

        public object Actor => _actor;

        public Hex CurrentAnchor => _actor?.Anchor ?? Hex.Zero;

        public void EnsurePlacedNow()
        {
            _driver?.EnsureInit();
            if (_occ == null && occupancyService)
                _occ = occupancyService.Get();
            if (!IsReady)
                return;
            if (_placed)
                return;

            _actor.Anchor = _driver.UnitRef.Position;
            _actor.Facing = _driver.UnitRef.Facing;

            if (_occ.TryPlace(_actor, _actor.Anchor, _actor.Facing))
            {
                _placed = true;
                if (debugLog)
                    Debug.Log($"[Occ] Place {IdLabel()} at {_actor.Anchor}", this);
                MirrorDriver(_actor.Anchor, _actor.Facing);
            }
        }

        public void MoveCommit(Hex newAnchor, Facing4 newFacing)
        {
            if (_occ == null && occupancyService)
                _occ = occupancyService.Get();
            if (!IsReady)
                return;

            if (_occ.TryMove(_actor, newAnchor))
            {
                _actor.Anchor = newAnchor;
                _actor.Facing = newFacing;
                _placed = true;
                if (debugLog)
                    Debug.Log($"[Occ] Move {IdLabel()} -> {newAnchor}", this);
                MirrorDriver(newAnchor, newFacing);
            }
            else
            {
                _occ.Remove(_actor);
                if (_occ.TryPlace(_actor, newAnchor, newFacing))
                {
                    _actor.Anchor = newAnchor;
                    _actor.Facing = newFacing;
                    _placed = true;
                    if (debugLog)
                        Debug.Log($"[Occ] RePlace {IdLabel()} at {newAnchor}", this);
                    MirrorDriver(newAnchor, newFacing);
                }
            }
        }

        IGridActor CreateActorAdapter()
        {
            var fp = overrideFootprint ? overrideFootprint : CreateSingle();
            return new BridgeActor(_driver, fp);
        }

        string IdLabel()
        {
            if (_actor != null)
                return _actor.Id;
            return _driver != null && !string.IsNullOrEmpty(_driver.unitId) ? _driver.unitId : "Player";
        }

        sealed class BridgeActor : IGridActor
        {
            readonly HexBoardTestDriver _driver;
            readonly FootprintShape _footprint;

            public BridgeActor(HexBoardTestDriver driver, FootprintShape footprint)
            {
                _driver = driver;
                _footprint = footprint ? footprint : PlayerOccupancyBridge.CreateSingle();
                if (driver != null && driver.UnitRef != null)
                {
                    Anchor = driver.UnitRef.Position;
                    Facing = driver.UnitRef.Facing;
                }
                else
                {
                    Anchor = Hex.Zero;
                    Facing = Facing4.PlusQ;
                }
            }

            public string Id
            {
                get
                {
                    if (_driver != null && !string.IsNullOrEmpty(_driver.unitId))
                        return _driver.unitId;
                    return "Player";
                }
            }

            public Hex Anchor { get; set; }
            public Facing4 Facing { get; set; }
            public FootprintShape Footprint => _footprint;
        }

        void MirrorDriver(Hex anchor, Facing4 facing)
        {
            if (_driver == null || !_driver.IsReady || _driver.UnitRef == null)
                return;

            var unit = _driver.UnitRef;
            unit.Position = anchor;
            unit.Facing = facing;

            if (_driver.Map != null)
                _driver.Map.Set(unit, anchor);

            _driver.SyncView();

            if (debugLog)
                Debug.Log($"[Occ] MirrorDriver -> {anchor} facing={facing}", this);
        }

        static FootprintShape CreateSingle()
        {
            var shape = ScriptableObject.CreateInstance<FootprintShape>();
            shape.name = "PlayerFootprint_Single_Runtime";
            shape.offsets = new() { new L2(0, 0) };
            return shape;
        }
    }
}
