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
        public HexOccupancyService occSvc;
        public FootprintShape overrideFootprint;
        public bool debugLog;
        public bool autoMirrorDebug = false;
        public bool useNewOcc = true;

        HexBoardTestDriver _driver;
        UnitRuntimeContext _ctx;
        UnitGridAdapter _componentAdapter;
        HexOccupancy _occ;
        IGridActor _actor;
        bool _placed;
        IOccupancyService _resolvedOcc;
        bool _loggedRoute;

        public event System.Action<Hex, int> AnchorChanged;

        public Hex CurrentAnchor => _actor?.Anchor ?? Hex.Zero;

        public int AnchorVersion { get; private set; } = 0;

        void Awake()
        {
            _driver = GetComponent<HexBoardTestDriver>();
            _ctx = GetComponent<UnitRuntimeContext>() ?? GetComponentInParent<UnitRuntimeContext>(true);
            _componentAdapter = GetComponent<UnitGridAdapter>() ?? GetComponentInChildren<UnitGridAdapter>(true);

            if (occSvc == null && occupancyService != null)
                occSvc = occupancyService;

            if (occSvc == null)
                occSvc = GetComponent<HexOccupancyService>() ?? GetComponentInParent<HexOccupancyService>(true);

            if (occSvc == null && _ctx != null)
                occSvc = _ctx.GetComponentInParent<HexOccupancyService>(true);

            if (occSvc == null && _driver != null)
            {
                occSvc = _driver.GetComponentInParent<HexOccupancyService>(true);
                if (occSvc == null && _driver.authoring != null)
                {
                    occSvc = _driver.authoring.GetComponent<HexOccupancyService>();
                    if (occSvc == null)
                        occSvc = _driver.authoring.GetComponentInParent<HexOccupancyService>(true);
                }
            }

            if (occupancyService == null)
                occupancyService = occSvc;

            if (useNewOcc)
                _resolvedOcc = ResolveService();
            else
                EnsureOccupancyBacking();

            EnsureActorBinding();
        }

        UnitRuntimeContext ResolveContext()
        {
            if (_ctx != null)
                return _ctx;

            _ctx = GetComponent<UnitRuntimeContext>()
                ?? GetComponentInParent<UnitRuntimeContext>(true)
                ?? GetComponentInChildren<UnitRuntimeContext>(true);
            return _ctx;
        }

        IOccupancyService ResolveService()
        {
            if (!useNewOcc)
                return null;

            if (_resolvedOcc != null)
                return _resolvedOcc;

            HexOccupancyService service = occSvc != null ? occSvc : occupancyService;

            if (service == null)
            {
                service = GetComponent<HexOccupancyService>() ?? GetComponentInParent<HexOccupancyService>(true);

                if (service == null)
                {
                    var ctx = ResolveContext();
                    if (ctx != null)
                        service = ctx.GetComponentInParent<HexOccupancyService>(true);
                }

                if (service == null && _driver != null)
                {
                    service = _driver.GetComponentInParent<HexOccupancyService>(true);
                    if (service == null && _driver.authoring != null)
                    {
                        service = _driver.authoring.GetComponent<HexOccupancyService>()
                                  ?? _driver.authoring.GetComponentInParent<HexOccupancyService>(true);
                    }
                }
            }

            if (service != null)
            {
                occSvc = service;
                if (occupancyService == null)
                    occupancyService = service;

                _resolvedOcc = service;
                if (!_loggedRoute)
                {
                    _loggedRoute = true;
                    Debug.Log($"[Occ] PlayerOccupancyBridge using {service.GetType().Name} via IOccupancyService", this);
                }
            }

            return _resolvedOcc;
        }

        HexBoardLayout ResolveLayout()
        {
            var service = occSvc != null ? occSvc : occupancyService;
            if (service != null && service.authoring != null)
                return service.authoring.Layout;
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
            if (!Application.isPlaying)
                return;

            if (useNewOcc)
            {
                var ctx = ResolveContext();
                var svc = ResolveService();
                if (ctx != null && svc != null && _placed)
                {
                    svc.Remove(ctx);
                    _placed = false;
                    if (debugLog)
                        Debug.Log($"[Occ] Remove {IdLabel()} via IOccupancyService", this);
                }
                return;
            }

            LegacyRemove();
        }

        public bool IsReady
        {
            get
            {
                if (useNewOcc)
                {
                    if (ResolveService() == null)
                        return false;

                    EnsureActorBinding();

                    if (_actor == null)
                        return false;

                    if (_componentAdapter != null && _componentAdapter.Unit != null)
                        return true;

                    var ctx = ResolveContext();
                    return ctx != null && ctx.boundUnit != null;
                }

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
            var unitRef = ResolveUnit();
            var anchor = unitRef != null ? unitRef.Position : (_actor != null ? _actor.Anchor : Hex.Zero);
            var face = unitRef != null ? unitRef.Facing : (_actor != null ? _actor.Facing : Facing4.PlusQ);
            if (_placed)
                return true;
            return TryPlaceImmediateInternal(anchor, face);
        }

        public void Bind(UnitGridAdapter adapter)
        {
            if (adapter == null)
                return;

            EnsureActorBinding(adapter);
            if (!useNewOcc)
                EnsureOccupancyBacking();
        }

        public bool PlaceImmediate(Hex anchor, Facing4 facing)
            => TryPlaceImmediateInternal(anchor, facing, _componentAdapter);

        public bool Register(Unit unit, Hex anchor, Facing4 facing)
        {
            if (unit != null && _componentAdapter != null && _componentAdapter.Unit == null)
                _componentAdapter.Unit = unit;

            return TryPlaceImmediateInternal(anchor, facing, _componentAdapter);
        }

        public bool MoveTo(Unit unit, Hex anchor, Facing4 facing)
            => MoveCommit(anchor, facing);

        public void Unregister(Unit unit)
        {
            if (useNewOcc)
            {
                var ctx = ResolveContext();
                var svc = ResolveService();
                if (ctx != null && svc != null)
                {
                    svc.Remove(ctx);
                    _placed = false;
                }
                return;
            }

            LegacyRemove();
        }

        public bool IsFree(Unit unit, Hex anchor, FootprintShape fp, Facing4 facing)
        {
            if (useNewOcc)
            {
                var svc = ResolveService();
                return svc != null && svc.IsFree(anchor, fp, facing);
            }

            EnsureOccupancyBacking();
            return _occ != null && _occ.IsFree(anchor, fp, facing);
        }

        public bool GetActor(Unit unit, Hex anchor, out IGridActor actor)
        {
            if (useNewOcc)
            {
                var svc = ResolveService();
                if (svc != null)
                    return svc.TryGetActor(anchor, out actor);

                actor = null;
                return false;
            }

            EnsureOccupancyBacking();
            if (_occ != null)
                return _occ.TryGetActor(anchor, out actor);

            actor = null;
            return false;
        }

        public bool MoveCommit(Hex newAnchor, Facing4 newFacing)
        {
            if (useNewOcc)
            {
                var ctx = ResolveContext();
                var svc = ResolveService();
                EnsureActorBinding();

                if (ctx == null || svc == null || _actor == null)
                    return false;

                bool success = svc.TryMove(ctx, newAnchor, newFacing);
                if (success)
                {
                    if (debugLog)
                        Debug.Log($"[Occ] Move {IdLabel()} -> {newAnchor}", this);

                    MirrorDriver(newAnchor, newFacing);
                    _placed = true;
                    RaiseAnchorChanged(newAnchor);
#if UNITY_EDITOR
                    ValidateConsistency(ctx, newAnchor);
#endif
                    return true;
                }

#if UNITY_EDITOR
                Debug.LogWarning($"[Occ] MoveCommit failed via IOccupancyService -> {newAnchor}", this);
#endif
                return false;
            }

            return LegacyMoveCommit(newAnchor, newFacing);
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
                if (overrideFootprint != null && _componentAdapter.Footprint == null)
                    _componentAdapter.Footprint = overrideFootprint;
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

        void EnsureOccupancyBacking()
        {
            if (_occ == null && occupancyService)
                _occ = occupancyService.Get();
            if (_occ == null)
            {
                var layout = ResolveLayout();
                if (layout != null)
                    _occ = new HexOccupancy(layout);
            }
        }

        void EnsureActorBinding(UnitGridAdapter explicitAdapter = null)
        {
            if (explicitAdapter != null)
                _componentAdapter = explicitAdapter;

            var ctx = ResolveContext();

            if (_componentAdapter == null && ctx != null)
                _componentAdapter = ctx.GetComponent<UnitGridAdapter>() ?? ctx.GetComponentInChildren<UnitGridAdapter>(true);

            if (_componentAdapter == null)
                _componentAdapter = GetComponent<UnitGridAdapter>() ?? GetComponentInChildren<UnitGridAdapter>(true);

            if (_componentAdapter != null)
            {
                if (overrideFootprint != null && _componentAdapter.Footprint == null)
                    _componentAdapter.Footprint = overrideFootprint;

                if (_componentAdapter.Unit == null)
                {
                    var unit = ctx != null && ctx.boundUnit != null ? ctx.boundUnit : null;
                    if (unit == null && _driver != null && _driver.IsReady)
                        unit = _driver.UnitRef;
                    if (unit != null)
                        _componentAdapter.Unit = unit;
                }

                _actor = _componentAdapter;
                return;
            }

            if (!useNewOcc && _actor == null)
                _actor = CreateActorAdapter();
        }

        bool TryPlaceImmediateInternal(Hex anchor, Facing4 facing, UnitGridAdapter explicitAdapter = null)
        {
            if (useNewOcc)
            {
                var ctx = ResolveContext();
                var svc = ResolveService();
                EnsureActorBinding(explicitAdapter);

                if (ctx != null && svc != null && _actor != null)
                {
                    bool placed = svc.TryPlace(ctx, anchor, facing);
                    if (!placed)
                        placed = svc.TryMove(ctx, anchor, facing);

                    if (placed)
                    {
                        if (debugLog)
                            Debug.Log($"[Occ] Place {IdLabel()} at {anchor} via IOccupancyService", this);

                        MirrorDriver(anchor, facing);
                        _placed = true;
                        RaiseAnchorChanged(anchor);
#if UNITY_EDITOR
                        ValidateConsistency(ctx, anchor);
#endif
                        return true;
                    }
                }

#if UNITY_EDITOR
                Debug.LogWarning($"[Occ] PlaceImmediate failed via IOccupancyService -> {anchor}", this);
#endif
                return false;
            }

            return LegacyTryPlaceImmediateInternal(anchor, facing, explicitAdapter);
        }

        bool LegacyTryPlaceImmediateInternal(Hex anchor, Facing4 facing, UnitGridAdapter explicitAdapter)
        {
            _driver?.EnsureInit();
            EnsureActorBinding(explicitAdapter);
            EnsureOccupancyBacking();

            if (!IsReady || _occ == null || _actor == null)
                return false;

            if (!_occ.TryPlace(_actor, anchor, facing))
                return false;

            _placed = true;
            if (debugLog)
                Debug.Log($"[Occ] Place {IdLabel()} at {anchor}", this);
            MirrorDriver(anchor, facing);
            RaiseAnchorChanged(anchor);
            return true;
        }

        bool LegacyMoveCommit(Hex newAnchor, Facing4 newFacing)
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
            else if (_occ != null)
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

            if (!success)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[Occ] MoveCommit failed -> {newAnchor}", this);
#endif
                return false;
            }

            if (debugLog)
            {
                string verb = replaced ? "RePlace" : "Move";
                Debug.Log($"[Occ] {verb} {IdLabel()} -> {newAnchor}", this);
            }

            MirrorDriver(_actor.Anchor, _actor.Facing);
            RaiseAnchorChanged(newAnchor);
            return true;
        }

        void LegacyRemove()
        {
            if (_occ != null && _actor != null && _placed)
            {
                _occ.Remove(_actor);
                _placed = false;
                if (debugLog)
                    Debug.Log($"[Occ] Remove {IdLabel()}", this);
            }
        }

#if UNITY_EDITOR
        void ValidateConsistency(UnitRuntimeContext ctx, Hex anchor)
        {
            if (ctx == null)
                return;

            var service = occSvc != null ? occSvc : occupancyService;
            var occ = service != null ? service.Get() : null;
            if (occ == null)
                return;

            if (ctx.boundUnit != null && !ctx.boundUnit.Position.Equals(anchor))
                Debug.LogWarning($"[Occ] Context/unit mismatch: ctx={ctx.name} unit={ctx.boundUnit.Position} occ={anchor}", ctx);
        }
#endif
    }
}
