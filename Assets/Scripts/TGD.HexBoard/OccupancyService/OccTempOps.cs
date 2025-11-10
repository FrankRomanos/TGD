// File: TGD.HexBoard/Occ/OccTempOps.cs
using UnityEngine;
using TGD.CoreV2;

namespace TGD.HexBoard
{
    /// 临时预留/清理的统一门面（不依赖 Combat；仍写同一份 HexOccupancy）
    public static class OccTempOps
    {
        static HexOccupancy TryGetStore(HexOccServiceAdapter ada)
        {
            return (ada != null && ada.backing != null) ? ada.backing.Get() : null;
        }

        // 清掉 ctx 对应 Actor 的所有临时预留；返回清理数量
        public static int ClearFor(UnitRuntimeContext ctx)
        {
            if (ctx == null) return 0;

            // 1) 首选：经由已注入的 IOcc 适配器
            var ada = ctx.occService as HexOccServiceAdapter;
            var store = TryGetStore(ada);
            if (ada != null && store != null && ada.actorResolver != null)
            {
                var actor = ada.actorResolver.GetOrBind(ctx); // UnitGridAdapter
                return (actor != null) ? store.TempClearForOwner(actor) : 0;
            }

            // 2) 兜底：直接在 HexBoard 场景内解析，不引用 Combat
            var resolver = Object.FindFirstObjectByType<OccActorResolver>(FindObjectsInactive.Include);
            var svc = Object.FindFirstObjectByType<HexOccupancyService>(FindObjectsInactive.Include);
            if (resolver != null && svc != null)
            {
                var a = resolver.GetOrBind(ctx);
                var s = svc.Get();
                return (a != null && s != null) ? s.TempClearForOwner(a) : 0;
            }

            return 0;
        }

        // 预留一个 cell 给 ctx 所属 Actor（用于路径高亮等）
        public static bool Reserve(UnitRuntimeContext ctx, Hex cell)
        {
            if (ctx == null) return false;

            var ada = ctx.occService as HexOccServiceAdapter;
            var store = TryGetStore(ada);
            if (ada != null && store != null && ada.actorResolver != null)
            {
                var actor = ada.actorResolver.GetOrBind(ctx);
                return actor != null && store.TempReserve(cell, actor);
            }

            var resolver = Object.FindFirstObjectByType<OccActorResolver>(FindObjectsInactive.Include);
            var svc = Object.FindFirstObjectByType<HexOccupancyService>(FindObjectsInactive.Include);
            if (resolver != null && svc != null)
            {
                var a = resolver.GetOrBind(ctx);
                var s = svc.Get();
                return (a != null && s != null) && s.TempReserve(cell, a);
            }

            return false;
        }
    }
}
