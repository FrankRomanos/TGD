using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    public sealed class HexOccupancyService : MonoBehaviour, IOccupancyService
    {
        [Header("Board Authoring")]
        public HexBoardAuthoringLite authoring;

        [SerializeField]
        string boardIdOverride;

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

        void Awake()
        {
            OccDiagnostics.AssertSingleStore(this, "HexOccupancyService.Awake");
        }

        void ResetState()
        {
            _nextTxnId = 1;
            _storeVersion = 0;
            _nextTokenId = 1;
            _activeTokens.Clear();
            _tokensByContext.Clear();
        }

        public HexOccupancy Get()
        {
            if (_occ == null && authoring != null && authoring.Layout != null)
            {
                _occ = new HexOccupancy(authoring.Layout);
                ResetState();
            }
            return _occ;
        }

        public bool Register(IGridActor actor, Hex anchor, Facing4 facing)
            => Get() != null && actor != null && Get().TryPlace(actor, anchor, facing);

        public void Unregister(IGridActor actor) { Get()?.Remove(actor); }

        OccTxnId NextTxn()
        {
            var id = new OccTxnId(_nextTxnId++);
            if (_nextTxnId < 0)
                _nextTxnId = 1;
            return id;
        }

        OccToken NextToken()
        {
            if (_nextTokenId == 0)
                _nextTokenId = 1;
            var token = new OccToken(_nextTokenId++);
            if (_nextTokenId == 0)
                _nextTokenId = 1;
            return token;
        }

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

        static bool HasCells(IReadOnlyList<Hex> cells)
            => cells != null && cells.Count > 0;

        bool WasPlaced(HexOccupancy store, IGridActor actor)
        {
            var cells = store?.CellsOf(actor);
            return HasCells(cells);
        }

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
            OccDiagnostics.Log(action, txn, actor?.Id ?? "?", from, anchor, OccFailReason.None);
            return true;
        }

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

                if (store.TryGetSoftOwner(cell, out var owner) && owner == actor)
                    continue;

                if (!store.TempReserveSoft(cell, actor))
                {
                    if (store.TryGetSoftOwner(cell, out owner) && owner == actor)
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
                    if (store.TryGetTempOwner(cell, out var owner) && owner == actor)
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

        public bool TryPlace(UnitRuntimeContext ctx, Hex anchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason)
        {
            txn = default;
            reason = OccFailReason.None;

            if (!TryResolveActor(ctx, out var actor, out reason))
                return false;

            return TryPlaceInternal(ctx, actor, anchor, facing, OccAction.Place, out txn, out reason);
        }

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
            OccDiagnostics.Log(OccAction.Remove, txn, actor.Id, from, Hex.Zero, OccFailReason.None);
            CancelAll(ctx, "Remove");
        }

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
            OccDiagnostics.LogReserve(token, actor.Id, reserved.Count, mode);
            result = OccReserveResult.Ok;
            return true;
        }

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
            OccDiagnostics.LogCommit(token, finalAnchor, reason);

            ReleaseReservation(reservation);
            UntrackToken(token, reservation.context);
            return ok;
        }

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
            OccDiagnostics.LogCancel(token, reason);
            return true;
        }

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

        public bool IsFreeFor(UnitRuntimeContext ctx, Hex anchor, Facing4 facing)
        {
            if (!TryResolveActor(ctx, out var actor, out _))
                return false;

            var store = Get();
            if (store == null)
                return false;

            return store.CanPlaceIgnoringTemp(actor, anchor, facing, actor);
        }

        public bool TryGetActorInfo(Hex anchor, out OccActorInfo info)
        {
            info = null;
            var store = Get();
            if (store == null)
                return false;

            if (!store.TryGetActor(anchor, out var actor) || actor == null)
                return false;

            string key = actor.Footprint != null ? actor.Footprint.name : null;
            info = new OccActorInfo(actor.Id, actor.Anchor, actor.Facing, key);
            return true;
        }

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
                string key = actor.Footprint != null ? actor.Footprint.name : null;
                list.Add(new OccSnapshot(BoardId, actor.Id, actor.Anchor, actor.Facing, key, _storeVersion));
            }
            return list.ToArray();
        }

        public int StoreVersion => _storeVersion;

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
