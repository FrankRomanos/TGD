using System;
using UnityEngine;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.CombatV2.Integration
{
    /// 仅为兼容旧代码的薄壳：所有实义均走 IOcc
    [DisallowMultipleComponent]
    [Obsolete("Replaced by IOcc. Thin shim kept for legacy callers; remove usages.", false)]
    public sealed class PlayerOccupancyBridge : MonoBehaviour, IActorOccupancyBridge
    {
        // === 兼容字段：仅为老代码编译通过 ===
        [Tooltip("Legacy compat: will be auto-resolved if null (do NOT rely on this).")]
        public HexOccupancyService occupancyService;

        [Header("Debug")]
        public bool debugLog;
        public bool shadowStrictBreak;

        UnitRuntimeContext _ctx;
        UnitGridAdapter _adapter;
        int _anchorVersion;

        public event Action<Hex, int> AnchorChanged;

        public bool IsReady => _ctx != null && _ctx.occService != null && _adapter != null;
        public object Actor => _adapter;
        public Hex CurrentAnchor => _adapter != null ? _adapter.Anchor : Hex.Zero;
        public int AnchorVersion => _anchorVersion;

        void Awake()
        {
            _ctx = GetComponent<UnitRuntimeContext>() ?? GetComponentInParent<UnitRuntimeContext>(true);
            _adapter = GetComponent<UnitGridAdapter>() ?? GetComponentInChildren<UnitGridAdapter>(true);

            if (occupancyService == null)
            {
#if UNITY_2023_1_OR_NEWER
                occupancyService = FindFirstObjectByType<HexOccupancyService>(FindObjectsInactive.Include);
#else
                occupancyService = FindObjectOfType<HexOccupancyService>();
#endif
            }
        }

        void OnEnable()
        {
            EnsurePlacedNow();
        }

        void OnDisable()
        {
            if (_ctx != null && _ctx.occService != null)
            {
                _ctx.occService.Remove(_ctx, out _);
            }
        }

        public bool EnsurePlacedNow()
        {
            if (!IsReady)
                return false;

            var anchor = _adapter.Anchor;
            var facing = _adapter.Facing;

            var ok = _ctx.occService.TryPlace(_ctx, anchor, facing, out _, out _);
            if (ok)
            {
                RaiseAnchorChanged(anchor);
                if (debugLog)
                    Debug.Log($"[BridgeShim] EnsurePlaced @ {anchor}", this);
            }
            return ok;
        }

        public bool PlaceImmediate(Hex anchor, Facing4 facing)
        {
            if (_ctx == null || _ctx.occService == null)
                return false;

            var ok = _ctx.occService.TryPlace(_ctx, anchor, facing, out _, out _);
            if (ok)
            {
                if (_adapter != null)
                {
                    _adapter.Anchor = anchor;
                    _adapter.Facing = facing;
                }
                RaiseAnchorChanged(anchor);
                if (debugLog)
                    Debug.Log($"[BridgeShim] PlaceImmediate -> {anchor}", this);
            }
            return ok;
        }

        public bool MoveCommit(Hex anchor, Facing4 facing, OccToken token = default)
        {
            if (_ctx == null || _ctx.occService == null)
                return false;

            bool ok;
            if (token.IsValid)
            {
                ok = _ctx.occService.Commit(_ctx, token, anchor, facing, out _, out _);
            }
            else
            {
                ok = _ctx.occService.TryMove(_ctx, anchor, facing, out _, out _);
            }

            if (ok)
            {
                if (_adapter != null)
                {
                    _adapter.Anchor = anchor;
                    _adapter.Facing = facing;
                }
                RaiseAnchorChanged(anchor);
                if (debugLog)
                    Debug.Log($"[BridgeShim] MoveCommit -> {anchor}", this);
            }
            else if (debugLog)
            {
                Debug.LogWarning($"[BridgeShim] MoveCommit failed -> {anchor}", this);
            }

            return ok;
        }

        public void SyncAfterIOccPlacement(Hex anchor, Facing4 facing)
        {
            if (_adapter != null)
            {
                _adapter.Anchor = anchor;
                _adapter.Facing = facing;
            }

            RaiseAnchorChanged(anchor);

            if (debugLog)
                Debug.Log($"[BridgeShim] SyncAfterIOccPlacement -> {anchor}", this);
        }

        void RaiseAnchorChanged(Hex anchor)
        {
            _anchorVersion++;
            AnchorChanged?.Invoke(anchor, _anchorVersion);
        }
    }
}
