using System.Collections.Generic;

namespace TGD.CoreV2
{
    // 仅用于查询/诊断的只读信息，不暴露 HexBoard 细节
    public sealed class OccActorInfo
    {
        public string ActorId;
        public Hex Anchor;
        public Facing4 Facing;
        public string FootprintKey; // 仅诊断展示
        public OccActorInfo(string id, Hex a, Facing4 f, string key)
        { ActorId = id; Anchor = a; Facing = f; FootprintKey = key; }
    }

    public readonly struct OccToken
    {
        public readonly ulong id;
        public OccToken(ulong i) { id = i; }
        public bool IsValid => id != 0;
        public override string ToString() => id.ToString();
    }

    public enum OccReserveResult
    {
        Ok = 0,
        AlreadyReserved,
        Blocked,
        NoStore,
        NoActor
    }

    public interface IOccupancyService
    {
        // 基于 ctx 的原子操作（Actor/Footprint 在实现侧解析）
        bool TryPlace(UnitRuntimeContext ctx, Hex anchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason);
        bool TryMove(UnitRuntimeContext ctx, Hex anchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason);
        void Remove(UnitRuntimeContext ctx, out OccTxnId txn);

        // 新增：软预留 → 提交 → 取消
        bool ReservePath(UnitRuntimeContext ctx, IReadOnlyList<Hex> cells, out OccToken token, out OccReserveResult result);
        bool Commit(UnitRuntimeContext ctx, OccToken token, Hex finalAnchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason);
        bool Cancel(UnitRuntimeContext ctx, OccToken token, string reason = null);
        int CancelAll(UnitRuntimeContext ctx, string reason = null);

        // 只读查询：以 ctx 为主体（实现侧从 ctx 解算 footprint）
        bool IsFreeFor(UnitRuntimeContext ctx, Hex anchor, Facing4 facing);

        // 诊断/调试：获取某格上的只读“Actor 信息”
        bool TryGetActorInfo(Hex anchor, out OccActorInfo info);

        OccSnapshot[] DumpAll();
        int StoreVersion { get; }
        string BoardId { get; }
    }
}
