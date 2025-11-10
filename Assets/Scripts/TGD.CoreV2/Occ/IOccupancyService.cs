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

    public interface IOccupancyService
    {
        // 基于 ctx 的原子操作（Actor/Footprint 在实现侧解析）
        bool TryPlace(UnitRuntimeContext ctx, Hex anchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason);
        bool TryMove(UnitRuntimeContext ctx, Hex anchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason);
        void Remove(UnitRuntimeContext ctx, out OccTxnId txn);

        // 只读查询：以 ctx 为主体（实现侧从 ctx 解算 footprint）
        bool IsFreeFor(UnitRuntimeContext ctx, Hex anchor, Facing4 facing);

        // 诊断/调试：获取某格上的只读“Actor 信息”
        bool TryGetActorInfo(Hex anchor, out OccActorInfo info);

        OccSnapshot[] DumpAll();
        int StoreVersion { get; }
        string BoardId { get; }
    }
}
