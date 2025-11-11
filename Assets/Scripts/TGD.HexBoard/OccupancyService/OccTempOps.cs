// File: TGD.HexBoard/OccTempOps.cs
using UnityEngine;
using TGD.CoreV2;

namespace TGD.HexBoard
{
    /// 仅用于“预览/高亮”的临时占位工具（不做正式写入）
    public static class OccTempOps
    {
        static HexOccupancyService FindService()
            => Object.FindFirstObjectByType<HexOccupancyService>(FindObjectsInactive.Include);

        static UnitGridAdapter FindActor(UnitRuntimeContext ctx)
            => ctx != null
                ? (ctx.GetComponent<UnitGridAdapter>()
                   ?? ctx.GetComponentInChildren<UnitGridAdapter>(true)
                   ?? ctx.GetComponentInParent<UnitGridAdapter>(true))
                : null;

        /// 清掉 ctx 对应 Actor 的所有临时预留；返回清理数量
        public static int ClearFor(UnitRuntimeContext ctx)
        {
            var actor = FindActor(ctx);
            var svc = FindService();
            var occ = svc != null ? svc.Get() : null;
            return (actor != null && occ != null) ? occ.TempClearForOwner(actor) : 0;
        }

        /// 预留一个 cell 给 ctx 所属 Actor（用于路径/范围高亮）
        public static bool Reserve(UnitRuntimeContext ctx, Hex cell)
        {
            var actor = FindActor(ctx);
            var svc = FindService();
            var occ = svc != null ? svc.Get() : null;
            return (actor != null && occ != null) && occ.TempReserve(cell, actor);
        }
    }
}
