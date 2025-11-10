using System;
using UnityEngine;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.CombatV2.Integration
{
    /// 仅为兼容旧代码的薄壳：所有实义均走 IOcc
    [DisallowMultipleComponent]
    [Obsolete("Replaced by IOcc. Thin shim kept for legacy callers; remove usages.", false)]
    public sealed class PlayerOccupancyBridge : MonoBehaviour
    {
        // === 兼容字段：仅为老代码编译通过 ===
        [Tooltip("Legacy compat: will be auto-resolved if null (do NOT rely on this).")]
        public HexOccupancyService occupancyService; // ★ 关键：补回这个字段

        [Header("Debug")]
        public bool debugLog;
        public bool shadowStrictBreak;

        UnitRuntimeContext _ctx;
        UnitGridAdapter _adapter;
        int _anchorVersion;

        public event Action<Hex, int> AnchorChanged;

        public object Actor => _adapter;
        public Hex CurrentAnchor => _adapter != null ? _adapter.Anchor : Hex.Zero;

        void Awake()
        {
            // 解析 ctx/adapter
            _ctx = GetComponent<UnitRuntimeContext>() ?? GetComponentInParent<UnitRuntimeContext>(true);
            _adapter = GetComponent<UnitGridAdapter>() ?? GetComponentInChildren<UnitGridAdapter>(true);

            // 兼容：若有人还取 bridge.occupancyService.Get()，给他一个实例，避免 NullRef
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
            // 旧习惯：一启用就“确保落账”，但内部实际走 IOcc
            EnsurePlacedNow();
        }

        void OnDisable()
        {
            if (_ctx != null && _ctx.occService != null)
                _ctx.occService.Remove(_ctx);
        }

        // === 兼容方法：名称保持不变，内部全部代理 IOcc ===

        public bool EnsurePlacedNow()
        {
            if (_ctx == null || _ctx.occService == null || _adapter == null) return false;
            var ok = _ctx.occService.TryPlace(_ctx, _adapter.Anchor, _adapter.Facing);
            if (ok)
            {
                _anchorVersion++;
                AnchorChanged?.Invoke(_adapter.Anchor, _anchorVersion);
                if (debugLog) Debug.Log($"[BridgeShim] EnsurePlaced @ {_adapter.Anchor}", this);
            }
            return ok;
        }

        public bool PlaceImmediate(Hex anchor, Facing4 facing)
        {
            if (_ctx == null || _ctx.occService == null) return false;
            var ok = _ctx.occService.TryPlace(_ctx, anchor, facing);
            if (ok)
            {
                if (_adapter != null) { _adapter.Anchor = anchor; _adapter.Facing = facing; }
                _anchorVersion++;
                AnchorChanged?.Invoke(anchor, _anchorVersion);
                if (debugLog) Debug.Log($"[BridgeShim] PlaceImmediate -> {anchor}", this);
            }
            return ok;
        }

        public bool MoveCommit(Hex anchor, Facing4 facing)
        {
            if (_ctx == null || _ctx.occService == null) return false;
            var ok = _ctx.occService.TryMove(_ctx, anchor, facing);
            if (ok)
            {
                if (_adapter != null) { _adapter.Anchor = anchor; _adapter.Facing = facing; }
                _anchorVersion++;
                AnchorChanged?.Invoke(anchor, _anchorVersion);
                if (debugLog) Debug.Log($"[BridgeShim] MoveCommit -> {anchor}", this);
            }
            else
            {
                if (debugLog) Debug.LogWarning($"[BridgeShim] MoveCommit failed -> {anchor}", this);
            }
            return ok;
        }
    }
}
