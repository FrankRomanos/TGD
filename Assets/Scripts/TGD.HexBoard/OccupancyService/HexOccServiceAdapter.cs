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

        public string BoardId { get { return boardId; } }
        public int StoreVersion { get { return _storeVersion; } }

        public bool TryPlace(UnitRuntimeContext ctx, Hex anchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason)
        { txn = new OccTxnId(0); reason = OccFailReason.NoStore; return false; }

        public bool TryMove(UnitRuntimeContext ctx, Hex anchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason)
        { txn = new OccTxnId(0); reason = OccFailReason.NoStore; return false; }

        public void Remove(UnitRuntimeContext ctx, out OccTxnId txn) { txn = new OccTxnId(0); }

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
