using System;
using UnityEngine;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.CombatV2.Integration
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class PlayerOccupancyBridge : MonoBehaviour, IActorOccupancyBridge
    {
        public HexOccupancyService occSvc;

        [SerializeField, Obsolete("Use occSvc for IOccupancyService injection.", false)]
        HexOccupancyService occupancyService;

        [SerializeField, Obsolete("Test driver fallback removed.", false)]
        HexBoardTestDriver legacyDriver;

        public FootprintShape overrideFootprint;
        public bool debugLog;
        public bool autoMirrorDebug;

        UnitRuntimeContext _ctx;
        UnitGridAdapter _componentAdapter;
        IGridActor _actor;
        bool _placed;
        IOccupancyService _resolvedOcc;
        bool _loggedRoute;
        bool _failedOccLogged;

        public event Action<Hex, int> AnchorChanged;

        public Hex CurrentAnchor => _actor?.Anchor ?? Hex.Zero;

        public int AnchorVersion { get; private set; }

        void Awake()
        {
            if (occSvc == null && occupancyService != null)
                occSvc = occupancyService;

            _ctx = GetComponent<UnitRuntimeContext>() ?? GetComponentInParent<UnitRuntimeContext>(true);
            _componentAdapter = GetComponent<UnitGridAdapter>() ?? GetComponentInChildren<UnitGridAdapter>(true);

            EnsureActorBinding();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (legacyDriver != null)
            {
                Debug.LogWarning("[Occ] PlayerOccupancyBridge cleared legacy HexBoardTestDriver reference.", this);
                legacyDriver = null;
            }

            if (occupancyService != null && occSvc == null)
                occSvc = occupancyService;
        }
#endif

        void Start() => EnsurePlacedNow();

        UnitRuntimeContext ResolveContext()
        {
            if (_ctx != null)
                return _ctx;

            _ctx = GetComponent<UnitRuntimeContext>() ?? GetComponentInParent<UnitRuntimeContext>(true);
            return _ctx;
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

            if (_componentAdapter == null)
                _componentAdapter = gameObject.AddComponent<UnitGridAdapter>();

            if (overrideFootprint != null && _componentAdapter.Footprint == null)
                _componentAdapter.Footprint = overrideFootprint;

            if (ctx != null && ctx.boundUnit != null && _componentAdapter.Unit != ctx.boundUnit)
                _componentAdapter.Unit = ctx.boundUnit;

            _actor = _componentAdapter;
        }

        public void Bind(UnitGridAdapter adapter)
        {
            EnsureActorBinding(adapter);
        }

        static HexOccupancyService FindSceneService()
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<HexOccupancyService>();
#else
            return FindObjectOfType<HexOccupancyService>();
#endif
        }

        IOccupancyService ResolveService()
        {
            if (_resolvedOcc != null)
                return _resolvedOcc;

            if (occSvc != null)
            {
                _resolvedOcc = occSvc;
            }
            else
            {
                var ctx = ResolveContext();
                if (ctx != null && ctx.occService != null)
                {
                    _resolvedOcc = ctx.occService;
                }
                else
                {
                    var found = FindSceneService();
                    if (found != null)
                    {
                        occSvc = found;
                        _resolvedOcc = found;
                    }
                }
            }

            if (_resolvedOcc != null)
            {
                var ctx = ResolveContext();
                if (ctx != null && ctx.occService != _resolvedOcc)
                    ctx.occService = _resolvedOcc;

                if (!_loggedRoute)
                {
                    _loggedRoute = true;
                    Debug.Log($"[Occ] PlayerOccupancyBridge using {_resolvedOcc.GetType().Name} via IOccupancyService", this);
                }

                _failedOccLogged = false;
            }
            else if (!_failedOccLogged)
            {
                _failedOccLogged = true;
                Debug.LogError("[Occ] PlayerOccupancyBridge failed to resolve IOccupancyService. Assign HexOccupancyService.", this);
            }

            return _resolvedOcc;
        }

        public bool IsReady
        {
            get
            {
                var svc = ResolveService();
                if (svc == null)
                    return false;

                EnsureActorBinding();

                if (_actor == null)
                    return false;

                var ctx = ResolveContext();
                if (ctx != null && ctx.boundUnit != null)
                    return true;

                return _componentAdapter != null && _componentAdapter.Unit != null;
            }
        }

        public object Actor => _actor;

        public bool EnsurePlacedNow()
        {
            var unit = ResolveUnit();
            var anchor = unit != null ? unit.Position : (_actor != null ? _actor.Anchor : Hex.Zero);
            var facing = unit != null ? unit.Facing : (_actor != null ? _actor.Facing : Facing4.PlusQ);

            if (_placed)
                return true;

            return TryPlaceImmediateInternal(anchor, facing);
        }

        void Update()
        {
            if (!_placed)
                EnsurePlacedNow();

#if UNITY_EDITOR
            if (!autoMirrorDebug || _actor == null)
                return;

            var unit = ResolveUnit();
            if (unit != null && !unit.Position.Equals(_actor.Anchor))
            {
                Debug.LogWarning($"[Occ] Drift detected: unit={unit.Position} occ={_actor.Anchor}. Auto-sync.", this);
                SyncUnit(_actor.Anchor, _actor.Facing);
            }
#endif
        }

        void OnDisable()
        {
            if (!Application.isPlaying)
                return;

            if (!_placed)
                return;

            var ctx = ResolveContext();
            var svc = ResolveService();
            if (ctx != null && svc != null)
            {
                svc.Remove(ctx);
                _placed = false;
                if (debugLog)
                    Debug.Log($"[Occ] Remove {IdLabel()} via IOccupancyService", this);
            }
        }

        public bool PlaceImmediate(Hex anchor, Facing4 facing)
            => TryPlaceImmediateInternal(anchor, facing, _componentAdapter);

        public bool Register(Unit unit, Hex anchor, Facing4 facing)
        {
            if (unit != null)
            {
                var ctx = ResolveContext();
                if (ctx != null && ctx.boundUnit == null)
                    ctx.boundUnit = unit;

                if (_componentAdapter != null && _componentAdapter.Unit == null)
                    _componentAdapter.Unit = unit;
            }

            return TryPlaceImmediateInternal(anchor, facing, _componentAdapter);
        }

        public bool MoveTo(Unit unit, Hex anchor, Facing4 facing)
            => MoveCommit(anchor, facing);

        public void Unregister(Unit unit)
        {
            var ctx = ResolveContext();
            var svc = ResolveService();
            if (ctx != null && svc != null && _placed)
            {
                svc.Remove(ctx);
                _placed = false;
            }
        }

        public bool IsFree(Unit unit, Hex anchor, FootprintShape fp, Facing4 facing)
        {
            var svc = ResolveService();
            return svc != null && svc.IsFree(anchor, fp, facing);
        }

        public bool GetActor(Unit unit, Hex anchor, out CoreV2.IGridActor actor)
        {
            var svc = ResolveService();
            if (svc != null)
                return svc.TryGetActor(anchor, out actor);

            actor = null;
            return false;
        }

        public bool MoveCommit(Hex newAnchor, Facing4 newFacing)
        {
            var ctx = ResolveContext();
            var svc = ResolveService();
            EnsureActorBinding();

            if (ctx == null || svc == null || _actor == null)
                return false;

            bool success = svc.TryMove(ctx, newAnchor, newFacing);
            if (success)
            {
                _placed = true;
                SyncUnit(newAnchor, newFacing);
                RaiseAnchorChanged(newAnchor);
#if UNITY_EDITOR
                ValidateConsistency(ctx, newAnchor);
#endif
                if (debugLog)
                    Debug.Log($"[Occ] Move {IdLabel()} -> {newAnchor}", this);
                return true;
            }

#if UNITY_EDITOR
            Debug.LogWarning($"[Occ] MoveCommit failed via IOccupancyService -> {newAnchor}", this);
#endif
            return false;
        }

        bool TryPlaceImmediateInternal(Hex anchor, Facing4 facing, UnitGridAdapter explicitAdapter = null)
        {
            var ctx = ResolveContext();
            var svc = ResolveService();
            EnsureActorBinding(explicitAdapter);

            if (ctx == null || svc == null || _actor == null)
                return false;

            bool placed = svc.TryPlace(ctx, anchor, facing);
            if (placed)
            {
                _placed = true;
                SyncUnit(anchor, facing);
                RaiseAnchorChanged(anchor);
#if UNITY_EDITOR
                ValidateConsistency(ctx, anchor);
#endif
                if (debugLog)
                    Debug.Log($"[Occ] Place {IdLabel()} at {anchor} via IOccupancyService", this);
                return true;
            }

#if UNITY_EDITOR
            Debug.LogWarning($"[Occ] PlaceImmediate failed via IOccupancyService -> {anchor}", this);
#endif
            return false;
        }

        void SyncUnit(Hex anchor, Facing4 facing)
        {
            if (_componentAdapter != null)
            {
                _componentAdapter.Anchor = anchor;
                _componentAdapter.Facing = facing;
            }

            var ctx = ResolveContext();
            if (ctx != null && ctx.boundUnit != null)
            {
                ctx.boundUnit.Position = anchor;
                ctx.boundUnit.Facing = facing;
            }
        }

        void RaiseAnchorChanged(Hex anchor)
        {
            AnchorVersion++;
            AnchorChanged?.Invoke(anchor, AnchorVersion);
            if (debugLog)
                Debug.Log($"[Occ] AnchorChanged v{AnchorVersion} -> {anchor}", this);
        }

        Unit ResolveUnit()
        {
            var ctx = ResolveContext();
            if (ctx != null && ctx.boundUnit != null)
            {
                if (_componentAdapter != null && _componentAdapter.Unit != ctx.boundUnit)
                    _componentAdapter.Unit = ctx.boundUnit;
                return ctx.boundUnit;
            }

            return _componentAdapter != null ? _componentAdapter.Unit : null;
        }

        string IdLabel()
        {
            var unit = ResolveUnit();
            if (unit != null && !string.IsNullOrEmpty(unit.Id))
                return unit.Id;

            if (_actor != null && !string.IsNullOrEmpty(_actor.Id))
                return _actor.Id;

            return _ctx != null ? _ctx.name : name;
        }

#if UNITY_EDITOR
        void ValidateConsistency(UnitRuntimeContext ctx, Hex anchor)
        {
            if (ctx?.boundUnit == null)
                return;

            if (!ctx.boundUnit.Position.Equals(anchor))
                Debug.LogWarning($"[Occ] Context/unit mismatch: ctx={ctx.name} unit={ctx.boundUnit.Position} occ={anchor}", ctx);
        }
#endif
    }
}
