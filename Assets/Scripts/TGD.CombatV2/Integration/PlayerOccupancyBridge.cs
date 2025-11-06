using UnityEngine;
using TGD.HexBoard;
using TGD.CoreV2;

namespace TGD.CombatV2.Integration
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class PlayerOccupancyBridge : MonoBehaviour, IActorOccupancyBridge
    {
        public HexOccupancyService occupancyService;
        public FootprintShape overrideFootprint;
        public bool debugLog;
        public bool autoMirrorDebug = false;

        HexBoardTestDriver _driver;
        UnitRuntimeContext _ctx;
        UnitGridAdapter _componentAdapter;
        HexOccupancy _occ;
        IGridActor _actor;
        bool _placed;

        public event System.Action<Hex, int> AnchorChanged;

        public Hex CurrentAnchor => _actor?.Anchor ?? Hex.Zero;

        public int AnchorVersion { get; private set; } = 0;

        void Awake()
        {
            _driver = GetComponent<HexBoardTestDriver>();
            _ctx = GetComponent<UnitRuntimeContext>();
            _componentAdapter = GetComponent<UnitGridAdapter>() ?? GetComponentInChildren<UnitGridAdapter>(true);

            if (!occupancyService)
                occupancyService = GetComponent<HexOccupancyService>();

            if (!occupancyService && _ctx != null)
                occupancyService = _ctx.GetComponentInParent<HexOccupancyService>(true);

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
            if (_occ == null)
            {
                var layout = ResolveLayout();
                if (layout != null)
                    _occ = new HexOccupancy(layout);
            }

            _actor = CreateActorAdapter();
        }

        HexBoardLayout ResolveLayout()
        {
            if (occupancyService != null && occupancyService.authoring != null)
                return occupancyService.authoring.Layout;
            if (_driver != null && _driver.authoring != null)
                return _driver.authoring.Layout;
            return null;
        }

        void Start() => EnsurePlacedNow();

        void RaiseAnchorChanged(Hex anchor)
        {
            AnchorVersion++;
            AnchorChanged?.Invoke(anchor, AnchorVersion);
            if (debugLog)
                Debug.Log($"[Occ] AnchorChanged v{AnchorVersion} -> {anchor}", this);
        }

        void Update()
        {
            if (!_placed)
                EnsurePlacedNow();

#if UNITY_EDITOR
            if (!autoMirrorDebug)
                return;

            var unit = ResolveUnit();
            if (unit != null && _placed)
            {
                var anchor = _actor.Anchor;
                if (!unit.Position.Equals(anchor))
                {
                    Debug.LogWarning($"[Occ] Drift detected: unit={unit.Position} occ={anchor}. Auto-mirror.", this);
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

        public bool IsReady
        {
            get
            {
                if (_occ == null || _actor == null)
                    return false;

                if (_componentAdapter != null)
                    return _componentAdapter.Unit != null;

                if (_driver != null)
                    return _driver.IsReady && _driver.UnitRef != null;

                return false;
            }
        }

        public object Actor => _actor;

        public bool EnsurePlacedNow()
        {
            _driver?.EnsureInit();
            if (_occ == null && occupancyService)
                _occ = occupancyService.Get();

            if (_actor == null)
                _actor = CreateActorAdapter();

            if (!IsReady)
                return false;
            if (_placed)
                return true;

            var unitRef = ResolveUnit();
            if (unitRef != null)
            {
                _actor.Anchor = unitRef.Position;
                _actor.Facing = unitRef.Facing;
            }

            if (_occ.TryPlace(_actor, _actor.Anchor, _actor.Facing))
            {
                _placed = true;
                if (debugLog)
                    Debug.Log($"[Occ] Place {IdLabel()} at {_actor.Anchor}", this);
                MirrorDriver(_actor.Anchor, _actor.Facing);
                RaiseAnchorChanged(_actor.Anchor);
                return true;
            }
            return false;
        }

        public bool MoveCommit(Hex newAnchor, Facing4 newFacing)
        {
            if (_occ == null && occupancyService)
                _occ = occupancyService.Get();
            if (!IsReady)
                return false;

            _occ?.TempClearForOwner(_actor);
            bool success = false;
            bool replaced = false;

            if (_occ.TryMove(_actor, newAnchor))
            {
                _actor.Anchor = newAnchor;
                _actor.Facing = newFacing;
                _placed = true;
                success = true;
            }
            else
            {
                if (_occ != null)
                {
                    _occ.Remove(_actor);
                    _occ.TempClearForOwner(_actor);
                    if (_occ.TryPlace(_actor, newAnchor, newFacing))
                    {
                        _actor.Anchor = newAnchor;
                        _actor.Facing = newFacing;
                        _placed = true;
                        success = true;
                        replaced = true;
                    }
                }
            }

            if (success)
            {
                if (debugLog)
                {
                    string verb = replaced ? "RePlace" : "Move";
                    Debug.Log($"[Occ] {verb} {IdLabel()} -> {newAnchor}", this);
                }
                MirrorDriver(_actor.Anchor, _actor.Facing);
                RaiseAnchorChanged(newAnchor);
                return true;
            }

#if UNITY_EDITOR
            Debug.LogWarning($"[Occ] MoveCommit failed -> {newAnchor}", this);
#endif
            return false;
        }

        Unit ResolveUnit()
        {
            if (_ctx != null && _ctx.boundUnit != null)
            {
                if (_componentAdapter != null && _componentAdapter.Unit != _ctx.boundUnit)
                    _componentAdapter.Unit = _ctx.boundUnit;
                return _ctx.boundUnit;
            }

            if (_componentAdapter != null && _componentAdapter.Unit != null)
                return _componentAdapter.Unit;

            if (_driver != null && _driver.IsReady)
                return _driver.UnitRef;

            return null;
        }

        IGridActor CreateActorAdapter()
        {
            if (_componentAdapter != null)
            {
                var unit = ResolveUnit();
                if (_componentAdapter.Unit == null && unit != null)
                    _componentAdapter.Unit = unit;
                return _componentAdapter;
            }

            var fp = overrideFootprint ? overrideFootprint : CreateSingle();
            return new BridgeActor(_driver, fp);
        }

        string IdLabel()
        {
            var unit = ResolveUnit();
            if (unit != null && !string.IsNullOrEmpty(unit.Id))
                return unit.Id;

            if (_actor != null && !string.IsNullOrEmpty(_actor.Id))
                return _actor.Id;

            if (_driver != null && !string.IsNullOrEmpty(_driver.unitId))
                return _driver.unitId;

            return "Unit";
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
            var unit = ResolveUnit();
            if (unit != null)
            {
                unit.Position = anchor;
                unit.Facing = facing;
            }

            if (_driver != null && _driver.IsReady && _driver.UnitRef != null)
            {
                var driverUnit = _driver.UnitRef;
                driverUnit.Position = anchor;
                driverUnit.Facing = facing;

                if (_driver.Map != null)
                    _driver.Map.Set(driverUnit, anchor);

                _driver.SyncView();
            }

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
