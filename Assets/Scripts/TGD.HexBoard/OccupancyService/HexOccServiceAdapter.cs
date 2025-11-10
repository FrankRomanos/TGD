// File: TGD.HexBoard/Occ/HexOccServiceAdapter.cs
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
            store.Remove(actor);
            _storeVersion++;
            OccDiagnostics.Log(OccAction.Remove, txn, actor.Id, actor.Anchor, Hex.Zero, OccFailReason.None);
        }

        OccTxnId NewTxn() { return new OccTxnId(_nextTxn++); }

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
    }
}
