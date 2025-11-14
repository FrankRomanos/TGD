using System;
using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    /// <summary>
    /// Concrete IOcc implementation backed by <see cref="HexOccupancy"/>.  This MonoBehaviour
    /// lives on the battlefield board and is treated as the authoritative source for every
    /// placement, movement, and reservation.  UnitFactory injects it into UnitRuntimeContext
    /// so gameplay controllers can drive IOcc without knowing anything about HexBoard internals.
    /// </summary>
    public sealed class HexOccupancyService : MonoBehaviour, IOccupancyService
    {
        [Header("Board Authoring")]
        public HexBoardAuthoringLite authoring;

        [SerializeField]
        string boardIdOverride;

        [Header("Diagnostics")]
        [SerializeField]
        bool diagnosticsLoggingEnabled;

        HexOccupancy _occ;
        int _nextTxnId = 1;
        int _storeVersion;
        ulong _nextTokenId = 1;

        readonly Dictionary<OccToken, TokenReservation> _activeTokens = new();
        readonly Dictionary<UnitRuntimeContext, List<OccToken>> _tokensByContext = new();

        struct TokenReservation
        {
            public UnitRuntimeContext context;
            public UnitGridAdapter actor;
            public List<Hex> cells;
            public OccReserveMode mode;
        }

        /// <summary>
        /// Guarantees the scene only has a single active occupancy service.  Mixing two stores
        /// would break the "唯一真相源" contract, so we fail loudly during boot.
        /// </summary>
        void Awake()
        {
            OccDiagnostics.AssertSingleStore(this, "HexOccupancyService.Awake");
        }

        /// <summary>
        /// Resets runtime counters and caches when the board is (re)constructed.  Called every
        /// time <see cref="Get"/> spins up a fresh HexOccupancy instance so diagnostics stay aligned.
        /// </summary>
        void ResetState()
        {
            _nextTxnId = 1;
            _storeVersion = 0;
            _nextTokenId = 1;
            _activeTokens.Clear();
            _tokensByContext.Clear();
        }

        /// <summary>
        /// Lazily constructs the backing <see cref="HexOccupancy"/> using the authoring layout.
        /// The service is designed to survive domain reloads and late binding, so callers always
        /// ask for the store instead of caching direct references.
        /// </summary>
        public HexOccupancy Get()
        {
            if (_occ == null && authoring != null && authoring.Layout != null)
            {
                _occ = new HexOccupancy(authoring.Layout);
                ResetState();
            }
            return _occ;
        }

        /// <summary>
        /// Registers a grid actor directly with the underlying store.  Only used by legacy
        /// bootstrapping paths; gameplay code should flow through <see cref="TryPlace"/>.
        /// </summary>
        public bool Register(IGridActor actor, Hex anchor, Facing4 facing)
            => Get() != null && actor != null && Get().TryPlace(actor, anchor, facing);

        /// <summary>
        /// Mirrors <see cref="Register"/> but tears down actors.  Used by editor tools when
        /// respawning boards.
        /// </summary>
        public void Unregister(IGridActor actor) { Get()?.Remove(actor); }

        /// <summary>
        /// Produces a monotonically increasing transaction id for diagnostics.  Wraps around to
        /// keep ids positive if we churn millions of operations during stress tests.
        /// </summary>
        OccTxnId NextTxn()
        {
            var id = new OccTxnId(_nextTxnId++);
            if (_nextTxnId < 0)
                _nextTxnId = 1;
            return id;
        }

        /// <summary>
        /// Generates the lightweight reservation token identifier.  Tokens intentionally skip 0
        /// so the struct's <see cref="OccToken.IsValid"/> check stays cheap.
        /// </summary>
        OccToken NextToken()
        {
            if (_nextTokenId == 0)
                _nextTokenId = 1;
            var token = new OccToken(_nextTokenId++);
            if (_nextTokenId == 0)
                _nextTokenId = 1;
            return token;
        }

        /// <summary>
        /// Resolves a runtime context into the <see cref="UnitGridAdapter"/> responsible for
        /// talking to HexOccupancy.  Factories inject ctx.boundUnit, so we hydrate adapters on
        /// demand if the factory skipped manual wiring.
        /// </summary>
        bool TryResolveActor(UnitRuntimeContext ctx, out UnitGridAdapter adapter, out OccFailReason reason)
        {
            adapter = null;
            reason = OccFailReason.None;

            if (ctx == null)
            {
                reason = OccFailReason.ActorMissing;
                return false;
            }

            adapter = ctx.GetComponent<UnitGridAdapter>()
                      ?? ctx.GetComponentInChildren<UnitGridAdapter>(true)
                      ?? ctx.GetComponentInParent<UnitGridAdapter>(true);

            if (adapter == null)
            {
                reason = OccFailReason.AdapterMissing;
                return false;
            }

            if (adapter.Unit == null && ctx.boundUnit != null)
                adapter.Unit = ctx.boundUnit;

            return true;
        }

        /// <summary>
        /// Records a live reservation so we can later enforce Cancel/Commit semantics even if the
        /// issuing controller disables.  Tokens are also grouped by context for quick CancelAll.
        /// </summary>
        void TrackToken(UnitRuntimeContext ctx, OccToken token, in TokenReservation reservation)
        {
            _activeTokens[token] = reservation;
            if (ctx == null)
                return;

            if (!_tokensByContext.TryGetValue(ctx, out var list) || list == null)
            {
                list = new List<OccToken>();
                _tokensByContext[ctx] = list;
            }

            if (!list.Contains(token))
                list.Add(token);
        }

        /// <summary>
        /// Drops tracking for a token and cleans up the per-context map when the last reservation
        /// disappears.  Keeps diagnostics noise-free and prevents memory churn in long battles.
        /// </summary>
        void UntrackToken(OccToken token, UnitRuntimeContext ctx)
        {
            _activeTokens.Remove(token);
            if (ctx == null)
                return;

            if (_tokensByContext.TryGetValue(ctx, out var list) && list != null)
            {
                list.Remove(token);
                if (list.Count == 0)
                    _tokensByContext.Remove(ctx);
            }
        }

        /// <summary>
        /// Centralized guard to avoid empty reservation requests.  Makes error handling consistent
        /// across soft and hard reservation modes.
        /// </summary>
        static bool HasCells(IReadOnlyList<Hex> cells)
            => cells != null && cells.Count > 0;

        /// <summary>
        /// Checks whether the actor already owns cells inside the store.  Used by move/remove
        /// flows to short-circuit before we attempt a write.
        /// </summary>
        bool WasPlaced(HexOccupancy store, IGridActor actor)
        {
            var cells = store?.CellsOf(actor);
            return HasCells(cells);
        }

        /// <summary>
        /// Shared implementation behind place/move/commit.  Handles validation, writes into the
        /// store, increments version counters, and emits diagnostics.  All public entry points
        /// funnel through here to keep transactional semantics consistent.
        /// </summary>
        bool TryPlaceInternal(UnitRuntimeContext ctx, UnitGridAdapter actor, Hex anchor, Facing4 facing, OccAction action, out OccTxnId txn, out OccFailReason reason)
        {
            txn = default;
            reason = OccFailReason.None;

            var store = Get();
            if (store == null)
            {
                reason = OccFailReason.NoStore;
                return false;
            }

            var from = actor != null ? actor.Anchor : Hex.Zero;

            if (!store.CanPlace(actor, anchor, facing, actor))
            {
                reason = OccFailReason.Blocked;
                return false;
            }

            if (!store.TryPlace(actor, anchor, facing))
            {
                reason = OccFailReason.Blocked;
                return false;
            }

            txn = NextTxn();
            _storeVersion++;
            if (diagnosticsLoggingEnabled)
                OccDiagnostics.Log(action, txn, actor?.Id ?? "?", from, anchor, OccFailReason.None);
            return true;
        }

        /// <summary>
        /// Releases whatever temporary cells a token held.  Handles both soft and hard modes so
        /// commit/cancel call sites do not need to duplicate release logic.
        /// </summary>
        void ReleaseReservation(TokenReservation reservation)
        {
            if (reservation.actor == null || reservation.cells == null || reservation.cells.Count == 0)
                return;

            var store = Get();
            if (store == null)
                return;

            switch (reservation.mode)
            {
                case OccReserveMode.SoftPath:
                    store.TempReleaseSoft(reservation.actor, reservation.cells);
                    break;
                default:
                    store.TempRelease(reservation.actor, reservation.cells);
                    break;
            }
        }

        /// <summary>
        /// Soft reservation flow used by most player previews.  Allows overlaps with the issuing
        /// actor while still rejecting cells blocked by others.  Newly added cells are tracked so
        /// we can roll back cleanly on failure.
        /// </summary>
        bool ReserveSoftPath(HexOccupancy store, UnitGridAdapter actor, IReadOnlyList<Hex> cells, List<Hex> newlyReserved)
        {
            bool anyNew = false;
            bool blocked = false;
            var seen = new HashSet<Hex>();

            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (!seen.Add(cell))
                    continue;

                if (store.IsBlockedFormal(cell, actor))
                {
                    blocked = true;
                    break;
                }

                if (store.TryGetSoftOwner(cell, out var owner) && ReferenceEquals(owner, actor))
                    continue;

                if (!store.TempReserveSoft(cell, actor))
                {
                    if (store.TryGetSoftOwner(cell, out owner) && ReferenceEquals(owner, actor))
                        continue;
                    blocked = true;
                    break;
                }

                newlyReserved.Add(cell);
                anyNew = true;
            }

            if (blocked)
            {
                if (newlyReserved.Count > 0)
                    store.TempReleaseSoft(actor, newlyReserved);
                return false;
            }

            return anyNew;
        }

        /// <summary>
        /// Hard reservation flow that optionally locks every step along the path.  Used by
        /// formation control, scripted sequences, and enemy rush logic where we must not be
        /// interrupted.  Similar rollback semantics to <see cref="ReserveSoftPath"/>.
        /// </summary>
        bool ReserveHardPath(HexOccupancy store, UnitGridAdapter actor, IReadOnlyList<Hex> cells, bool pathHard, List<Hex> newlyReserved)
        {
            bool anyNew = false;
            bool blocked = false;
            var seen = new HashSet<Hex>();
            int last = cells.Count - 1;

            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (!seen.Add(cell))
                    continue;

                bool shouldReserve = pathHard || i == last;

                if (store.IsBlockedFormal(cell, actor))
                {
                    blocked = true;
                    break;
                }

                if (!shouldReserve)
                    continue;

                if (!store.TempReserve(cell, actor))
                {
                    if (store.TryGetTempOwner(cell, out var owner) && ReferenceEquals(owner, actor))
                        continue;
                    blocked = true;
                    break;
                }

                newlyReserved.Add(cell);
                anyNew = true;
            }

            if (blocked)
            {
                if (newlyReserved.Count > 0)
                    store.TempRelease(actor, newlyReserved);
                return false;
            }

            return anyNew;
        }

        /// <inheritdoc />
        public bool TryPlace(UnitRuntimeContext ctx, Hex anchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason)
        {
            txn = default;
            reason = OccFailReason.None;

            if (!TryResolveActor(ctx, out var actor, out reason))
                return false;

            return TryPlaceInternal(ctx, actor, anchor, facing, OccAction.Place, out txn, out reason);
        }

        /// <inheritdoc />
        public bool TryMove(UnitRuntimeContext ctx, Hex anchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason)
        {
            txn = default;
            reason = OccFailReason.None;

            if (!TryResolveActor(ctx, out var actor, out reason))
                return false;

            var store = Get();
            if (store == null)
            {
                reason = OccFailReason.NoStore;
                return false;
            }

            if (!WasPlaced(store, actor))
            {
                reason = OccFailReason.NotPlaced;
                return false;
            }

            return TryPlaceInternal(ctx, actor, anchor, facing, OccAction.Move, out txn, out reason);
        }

        /// <inheritdoc />
        public void Remove(UnitRuntimeContext ctx, out OccTxnId txn)
        {
            txn = default;
            if (!TryResolveActor(ctx, out var actor, out _))
                return;

            var store = Get();
            if (store == null)
                return;

            if (!WasPlaced(store, actor))
                return;

            var from = actor.Anchor;
            store.Remove(actor);
            _storeVersion++;
            txn = NextTxn();
            if (diagnosticsLoggingEnabled)
                OccDiagnostics.Log(OccAction.Remove, txn, actor.Id, from, Hex.Zero, OccFailReason.None);
            CancelAll(ctx, "Remove");
        }

        /// <inheritdoc />
        public bool ReservePath(UnitRuntimeContext ctx, IReadOnlyList<Hex> cells, out OccToken token, out OccReserveResult result, OccReserveMode mode = OccReserveMode.SoftPath)
        {
            token = default;
            result = OccReserveResult.NoActor;

            if (!TryResolveActor(ctx, out var actor, out _))
            {
                result = OccReserveResult.NoActor;
                return false;
            }

            var store = Get();
            if (store == null)
            {
                result = OccReserveResult.NoStore;
                return false;
            }

            if (!HasCells(cells))
            {
                result = OccReserveResult.Blocked;
                return false;
            }

            var reserved = new List<Hex>();
            bool anyNew = false;
            bool ok;

            switch (mode)
            {
                case OccReserveMode.SoftPath:
                    ok = ReserveSoftPath(store, actor, cells, reserved);
                    anyNew = ok;
                    break;
                case OccReserveMode.EndOnlyHard:
                    ok = ReserveHardPath(store, actor, cells, false, reserved);
                    anyNew = ok;
                    break;
                case OccReserveMode.PathHard:
                    ok = ReserveHardPath(store, actor, cells, true, reserved);
                    anyNew = ok;
                    break;
                default:
                    ok = false;
                    break;
            }

            if (!ok)
            {
                if (reserved.Count == 0)
                {
                    result = OccReserveResult.AlreadyReserved;
                    return false;
                }

                result = OccReserveResult.Blocked;
                return false;
            }

            if (!anyNew)
            {
                result = OccReserveResult.AlreadyReserved;
                return false;
            }

            token = NextToken();
            var reservation = new TokenReservation
            {
                context = ctx,
                actor = actor,
                cells = reserved,
                mode = mode
            };
            TrackToken(ctx, token, reservation);
            if (diagnosticsLoggingEnabled)
                OccDiagnostics.LogReserve(token, actor.Id, reserved.Count, mode);
            result = OccReserveResult.Ok;
            return true;
        }

        /// <inheritdoc />
        public bool Commit(UnitRuntimeContext ctx, OccToken token, Hex finalAnchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason)
        {
            txn = default;
            reason = OccFailReason.None;

            if (!token.IsValid)
            {
                reason = OccFailReason.NotPlaced;
                return false;
            }

            if (!_activeTokens.TryGetValue(token, out var reservation))
            {
                reason = OccFailReason.NotPlaced;
                return false;
            }

            var ownerCtx = reservation.context != null ? reservation.context : ctx;
            if (ctx != null && reservation.context != null && !ReferenceEquals(ctx, reservation.context))
            {
                reason = OccFailReason.MultiStoreMismatch;
                return false;
            }

            if (!TryResolveActor(ownerCtx, out var actor, out reason))
                return false;

            var ok = TryPlaceInternal(ownerCtx, actor, finalAnchor, facing, OccAction.Commit, out txn, out reason);
            if (diagnosticsLoggingEnabled)
                OccDiagnostics.LogCommit(token, finalAnchor, reason);

            ReleaseReservation(reservation);
            UntrackToken(token, reservation.context);
            return ok;
        }

        /// <inheritdoc />
        public bool Cancel(UnitRuntimeContext ctx, OccToken token, string reason = null)
        {
            if (!token.IsValid)
                return false;

            if (!_activeTokens.TryGetValue(token, out var reservation))
                return false;

            if (ctx != null && reservation.context != null && !ReferenceEquals(ctx, reservation.context))
                return false;

            ReleaseReservation(reservation);
            UntrackToken(token, reservation.context);
            if (diagnosticsLoggingEnabled)
                OccDiagnostics.LogCancel(token, reason);
            return true;
        }

        /// <inheritdoc />
        public int CancelAll(UnitRuntimeContext ctx, string reason = null)
        {
            if (ctx == null)
                return 0;

            if (!_tokensByContext.TryGetValue(ctx, out var tokens) || tokens == null || tokens.Count == 0)
                return 0;

            var snapshot = tokens.ToArray();
            int count = 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (Cancel(ctx, snapshot[i], reason))
                    count++;
            }
            return count;
        }

        /// <inheritdoc />
        public bool IsFreeFor(UnitRuntimeContext ctx, Hex anchor, Facing4 facing)
        {
            if (!TryResolveActor(ctx, out var actor, out _))
                return false;

            var store = Get();
            if (store == null)
                return false;

            return store.CanPlaceIgnoringTemp(actor, anchor, facing, actor);
        }

        /// <inheritdoc />
        public bool TryGetActorInfo(Hex anchor, out OccActorInfo info)
        {
            info = null;
            var store = Get();
            if (store == null)
                return false;

            if (!store.TryGetActor(anchor, out var actor) || actor == null)
                return false;

            info = new OccActorInfo(actor.Id, actor.Anchor, actor.Facing, null);
            return true;
        }

        /// <inheritdoc />
        public OccSnapshot[] DumpAll()
        {
            var store = Get();
            if (store == null)
                return System.Array.Empty<OccSnapshot>();

            var list = new List<OccSnapshot>();
            foreach (var actor in store.EnumerateActors())
            {
                if (actor == null)
                    continue;
                list.Add(new OccSnapshot(BoardId, actor.Id, actor.Anchor, actor.Facing, null, _storeVersion));
            }
            return list.ToArray();
        }

        /// <inheritdoc />
        public int StoreVersion => _storeVersion;

        /// <summary>
        /// Human-readable identifier for diagnostics and replay dumps.  Prefers explicit overrides,
        /// then the authoring asset name, and finally falls back to the active scene.
        /// </summary>
        public string BoardId
        {
            get
            {
                if (!string.IsNullOrEmpty(boardIdOverride))
                    return boardIdOverride;
                if (authoring != null)
                    return authoring.name;
                var scene = gameObject.scene;
                return scene.IsValid() ? scene.name : name;
            }
        }
    }
}
