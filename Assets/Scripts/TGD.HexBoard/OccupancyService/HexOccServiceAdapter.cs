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

        public bool IsFreeFor(UnitRuntimeContext ctx, Hex anchor, Facing4 facing) { return false; }

        public bool TryGetActorInfo(Hex anchor, out OccActorInfo info) { info = null; return false; }

        public OccSnapshot[] DumpAll() { return new OccSnapshot[0]; }
    }
}
