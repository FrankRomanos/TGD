// File: TGD.HexBoard/Occ/HexOccServiceAdapter.cs
using System.Collections.Generic;
using UnityEngine;
using TGD.CoreV2;

namespace TGD.HexBoard
{
    [DisallowMultipleComponent]
    public sealed class HexOccServiceAdapter : MonoBehaviour, IOccupancyService
    {
        public HexOccupancyService backing;   // 唯一 Store 提供者
        public OccActorResolver actorResolver; // 解析 ctx -> UnitGridAdapter
        public string boardId = "Board-1";
        int _storeVersion;
        int _nextTxn = 1;
        ulong _nextToken = 1;

        readonly Dictionary<OccToken, HashSet<Hex>> _tokenToCells = new();
        readonly Dictionary<IGridActor, List<OccToken>> _actorToTokens = new();

        static readonly List<HexOccServiceAdapter> _snapshotCandidates = new();
        static HexOccServiceAdapter _snapshotOwner;

        void OnEnable()
        {
            if (!_snapshotCandidates.Contains(this))
                _snapshotCandidates.Add(this);

            if (_snapshotOwner == null)
                InstallSnapshotProvider();
        }

        void OnDisable()
        {
            _snapshotCandidates.Remove(this);

            if (ReferenceEquals(_snapshotOwner, this))
            {
                OccDiagnostics.RegisterTokenSnapshotProvider(null);
                _snapshotOwner = null;
                PromoteNextSnapshotOwner();
            }
        }

        void InstallSnapshotProvider()
        {
            OccDiagnostics.RegisterTokenSnapshotProvider(GetTokenLedgerSnapshot);
            _snapshotOwner = this;
        }

        static void PromoteNextSnapshotOwner()
        {
            for (int i = _snapshotCandidates.Count - 1; i >= 0; i--)
            {
                var candidate = _snapshotCandidates[i];
                if (candidate == null)
                {
                    _snapshotCandidates.RemoveAt(i);
                    continue;
                }

                if (!candidate.isActiveAndEnabled)
                    continue;

                candidate.InstallSnapshotProvider();
                break;
            }
        }

        public string BoardId { get { return boardId; } }
        public int StoreVersion { get { return _storeVersion; } }

        public bool TryPlace(UnitRuntimeContext ctx, Hex anchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason)
        {
            txn = NewTxn();
            reason = OccFailReason.None;

            var store = (backing != null) ? backing.Get() : null;
            if (store == null)
            {
                reason = OccFailReason.NoStore;
                OccDiagnostics.Log(OccAction.Place, txn, ctx ? ctx.name : "ctx-null", Hex.Zero, anchor, reason);
                return false;
            }

            if (actorResolver == null || ctx == null)
            {
                reason = OccFailReason.ActorMissing;
                OccDiagnostics.Log(OccAction.Place, txn, "null", Hex.Zero, anchor, reason);
                return false;
            }

            var actor = actorResolver.GetOrBind(ctx);
            if (actor == null)
            {
                reason = OccFailReason.ActorMissing;
                OccDiagnostics.Log(OccAction.Place, txn, "null", Hex.Zero, anchor, reason);
                return false;
            }
            if (!store.CanPlace(actor, anchor, facing, actor))
            {
                reason = OccFailReason.Blocked;
                OccDiagnostics.Log(OccAction.Place, txn, actor.Id, actor.Anchor, anchor, reason);
                return false;
            }

            bool ok = store.TryPlace(actor, anchor, facing);
            if (ok)
                _storeVersion++;

            OccDiagnostics.Log(OccAction.Place, txn, actor.Id, actor.Anchor, anchor, ok ? OccFailReason.None : OccFailReason.Blocked);
            return ok;
        }

        public bool TryMove(UnitRuntimeContext ctx, Hex anchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason)
        {
            txn = NewTxn();
            reason = OccFailReason.None;

            var store = (backing != null) ? backing.Get() : null;
            if (store == null)
            {
                reason = OccFailReason.NoStore;
                OccDiagnostics.Log(OccAction.Move, txn, ctx ? ctx.name : "ctx-null", Hex.Zero, anchor, reason);
                return false;
            }

            if (actorResolver == null || ctx == null)
            {
                reason = OccFailReason.ActorMissing;
                OccDiagnostics.Log(OccAction.Move, txn, "null", Hex.Zero, anchor, reason);
                return false;
            }

            var actor = actorResolver.GetOrBind(ctx);
            if (actor == null)
            {
                reason = OccFailReason.ActorMissing;
                OccDiagnostics.Log(OccAction.Move, txn, "null", Hex.Zero, anchor, reason);
                return false;
            }

            var from = actor.Anchor;
            if (anchor.Equals(from))
            {
                actor.Facing = facing;
                OccDiagnostics.Log(OccAction.Move, txn, actor.Id, from, anchor, OccFailReason.None);
                return true;
            }

            bool ok = store.TryMove(actor, anchor);
            if (ok)
            {
                actor.Facing = facing;
            }
            else if (store.CanPlace(actor, anchor, facing, actor))
            {
                ok = store.TryPlace(actor, anchor, facing);
            }
            else
            {
                reason = OccFailReason.Blocked;
            }

            if (!ok && reason == OccFailReason.None)
                reason = OccFailReason.Blocked;

            if (ok)
                _storeVersion++;

            OccDiagnostics.Log(OccAction.Move, txn, actor.Id, actor.Anchor, anchor, reason);
            return ok;
        }

        public void Remove(UnitRuntimeContext ctx, out OccTxnId txn)
        {
            txn = NewTxn();

            var store = (backing != null) ? backing.Get() : null;
            if (store == null)
            {
                OccDiagnostics.Log(OccAction.Remove, txn, ctx ? ctx.name : "ctx-null", Hex.Zero, Hex.Zero, OccFailReason.NoStore);
                return;
            }

            if (actorResolver == null || ctx == null)
            {
                OccDiagnostics.Log(OccAction.Remove, txn, "null", Hex.Zero, Hex.Zero, OccFailReason.ActorMissing);
                return;
            }

            var actor = actorResolver.GetOrBind(ctx);
            if (actor == null)
            {
                OccDiagnostics.Log(OccAction.Remove, txn, "null", Hex.Zero, Hex.Zero, OccFailReason.ActorMissing);
                return;
            }
            CancelAll(ctx, "Remove");
            store.Remove(actor);
            _storeVersion++;
            OccDiagnostics.Log(OccAction.Remove, txn, actor.Id, actor.Anchor, Hex.Zero, OccFailReason.None);
        }

        public bool ReservePath(UnitRuntimeContext ctx, IReadOnlyList<Hex> cells, out OccToken token, out OccReserveResult result)
        {
            token = default;
            result = OccReserveResult.Ok;

            var store = (backing != null) ? backing.Get() : null;
            if (store == null)
            {
                result = OccReserveResult.NoStore;
                return false;
            }

            if (actorResolver == null || ctx == null)
            {
                result = OccReserveResult.NoActor;
                return false;
            }

            var actor = actorResolver.GetOrBind(ctx);
            if (actor == null)
            {
                result = OccReserveResult.NoActor;
                return false;
            }

            var unique = new List<Hex>();
            var seen = new HashSet<Hex>();
            if (cells != null)
            {
                for (int i = 0; i < cells.Count; i++)
                {
                    var cell = cells[i];
                    if (seen.Add(cell))
                        unique.Add(cell);
                }
            }

            var owned = new HashSet<Hex>();
            if (_actorToTokens.TryGetValue(actor, out var ownedTokens) && ownedTokens != null)
            {
                for (int i = 0; i < ownedTokens.Count; i++)
                {
                    var tk = ownedTokens[i];
                    if (_tokenToCells.TryGetValue(tk, out var tokenCells) && tokenCells != null)
                        owned.UnionWith(tokenCells);
                }
            }

            if (owned.Count > 0)
            {
                for (int i = 0; i < unique.Count; i++)
                {
                    if (owned.Contains(unique[i]))
                    {
                        result = OccReserveResult.AlreadyReserved;
                        return false;
                    }
                }
            }

            var reservedNow = new List<Hex>();
            for (int i = 0; i < unique.Count; i++)
            {
                var cell = unique[i];
                if (store.TempReserve(cell, actor))
                {
                    reservedNow.Add(cell);
                    continue;
                }

                if (reservedNow.Count > 0)
                    store.TempRelease(actor, reservedNow);
                result = OccReserveResult.Blocked;
                return false;
            }

            token = NewToken();
            var ledgerCells = unique.Count > 0 ? new HashSet<Hex>(unique) : new HashSet<Hex>();
            _tokenToCells[token] = ledgerCells;

            if (!_actorToTokens.TryGetValue(actor, out var list) || list == null)
            {
                list = new List<OccToken>();
                _actorToTokens[actor] = list;
            }
            list.Add(token);

            OccDiagnostics.LogReserve(token, actor.Id, ledgerCells.Count);
            return true;
        }

        public bool Commit(UnitRuntimeContext ctx, OccToken token, Hex finalAnchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason)
        {
            txn = default;
            reason = OccFailReason.None;

            var store = (backing != null) ? backing.Get() : null;
            if (store == null)
            {
                reason = OccFailReason.NoStore;
                OccDiagnostics.LogCommit(token, finalAnchor, reason);
                return false;
            }

            if (actorResolver == null || ctx == null)
            {
                reason = OccFailReason.ActorMissing;
                OccDiagnostics.LogCommit(token, finalAnchor, reason);
                return false;
            }

            var actor = actorResolver.GetOrBind(ctx);
            if (actor == null)
            {
                reason = OccFailReason.ActorMissing;
                OccDiagnostics.LogCommit(token, finalAnchor, reason);
                return false;
            }

            if (!token.IsValid || !_tokenToCells.TryGetValue(token, out var reservedCells))
            {
                reason = OccFailReason.Blocked;
                OccDiagnostics.LogCommit(token, finalAnchor, reason);
                return false;
            }

            if (!_actorToTokens.TryGetValue(actor, out var tokenList) || tokenList == null || !tokenList.Contains(token))
            {
                reason = OccFailReason.Blocked;
                OccDiagnostics.LogCommit(token, finalAnchor, reason);
                return false;
            }

            bool ok = TryMove(ctx, finalAnchor, facing, out txn, out reason);
            if (ok)
            {
                store.TempClearForOwner(actor);
                _tokenToCells.Remove(token);
                tokenList.Remove(token);
                if (tokenList.Count == 0)
                    _actorToTokens.Remove(actor);
            }

            OccDiagnostics.LogCommit(token, finalAnchor, reason);
            return ok;
        }

        public bool Cancel(UnitRuntimeContext ctx, OccToken token, string reasonTag = null)
        {
            if (!token.IsValid)
                return false;

            var store = (backing != null) ? backing.Get() : null;
            if (store == null)
            {
                OccDiagnostics.LogCancel(token, reasonTag ?? "NoStore");
                return false;
            }

            if (actorResolver == null || ctx == null)
            {
                OccDiagnostics.LogCancel(token, reasonTag ?? "NoActor");
                return false;
            }

            var actor = actorResolver.GetOrBind(ctx);
            if (actor == null)
            {
                OccDiagnostics.LogCancel(token, reasonTag ?? "NoActor");
                return false;
            }

            if (!_tokenToCells.TryGetValue(token, out var cells))
            {
                OccDiagnostics.LogCancel(token, reasonTag ?? "MissingToken");
                return false;
            }

            store.TempRelease(actor, cells);
            _tokenToCells.Remove(token);

            if (_actorToTokens.TryGetValue(actor, out var list) && list != null)
            {
                list.Remove(token);
                if (list.Count == 0)
                    _actorToTokens.Remove(actor);
            }

            OccDiagnostics.LogCancel(token, reasonTag);
            return true;
        }

        public int CancelAll(UnitRuntimeContext ctx, string reasonTag = null)
        {
            var store = (backing != null) ? backing.Get() : null;

            if (actorResolver == null || ctx == null)
                return 0;

            var actor = actorResolver.GetOrBind(ctx);
            if (actor == null)
                return 0;

            if (!_actorToTokens.TryGetValue(actor, out var list) || list == null || list.Count == 0)
            {
                store?.TempClearForOwner(actor);
                return 0;
            }

            int count = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var token = list[i];
                _tokenToCells.Remove(token);
                OccDiagnostics.LogCancel(token, reasonTag ?? "CancelAll");
                count++;
            }

            _actorToTokens.Remove(actor);
            store?.TempClearForOwner(actor);
            return count;
        }

        OccTxnId NewTxn() { return new OccTxnId(_nextTxn++); }
        OccToken NewToken() { return new OccToken(++_nextToken); }

        public bool IsFreeFor(UnitRuntimeContext ctx, Hex anchor, Facing4 facing)
        {
            var store = (backing != null) ? backing.Get() : null;
            if (store == null || actorResolver == null || ctx == null)
                return false;

            var adapter = actorResolver.GetOrBind(ctx);
            if (adapter == null)
                return false;

            return store.CanPlaceIgnoringTemp(adapter, anchor, facing);
        }

        public bool TryGetActorInfo(Hex anchor, out OccActorInfo info)
        {
            info = null;
            var store = (backing != null) ? backing.Get() : null;
            if (store == null)
                return false;

            IGridActor actor;
            if (!store.TryGetActor(anchor, out actor) || actor == null)
                return false;

            var key = (actor.Footprint != null) ? actor.Footprint.name : "Single";
            info = new OccActorInfo(actor.Id, actor.Anchor, actor.Facing, key);
            return true;
        }

        public OccSnapshot[] DumpAll()
        {
            try
            {
                var adapters = FindObjectsByType<UnitGridAdapter>(
FindObjectsInactive.Include, FindObjectsSortMode.None);
                var list = new System.Collections.Generic.List<OccSnapshot>(adapters.Length);
                for (int i = 0; i < adapters.Length; i++)
                {
                    var adapter = adapters[i];
                    var key = (adapter.Footprint != null) ? adapter.Footprint.name : "Single";
                    list.Add(new OccSnapshot(BoardId, adapter.Id, adapter.Anchor, adapter.Facing, key, StoreVersion));
                }

                return list.ToArray();
            }
            catch
            {
                return new OccSnapshot[0];
            }
        }

        OccDiagnostics.OccTokenLedgerSnapshot GetTokenLedgerSnapshot()
        {
            if (_tokenToCells.Count == 0)
                return OccDiagnostics.OccTokenLedgerSnapshot.Empty;

            var owners = new List<OccDiagnostics.OccTokenOwnerSnapshot>(_actorToTokens.Count);
            foreach (var kv in _actorToTokens)
            {
                var tokens = kv.Value;
                if (tokens == null || tokens.Count == 0)
                    continue;

                int validCount = 0;
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (_tokenToCells.ContainsKey(tokens[i]))
                        validCount++;
                }

                if (validCount <= 0)
                    continue;

                string actorId = kv.Key != null ? kv.Key.Id : "<null>";
                owners.Add(new OccDiagnostics.OccTokenOwnerSnapshot(actorId, validCount));
            }

            return new OccDiagnostics.OccTokenLedgerSnapshot(_tokenToCells.Count, owners);
        }
    }
}
