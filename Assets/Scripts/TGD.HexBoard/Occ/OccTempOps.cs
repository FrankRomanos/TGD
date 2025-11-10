using System.Reflection;
using TGD.CombatV2.Integration;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    /// 统一的“临时预留/清理”操作门面（仍然写入同一份 HexOccupancy）
    public static class OccTempOps
    {
        const BindingFlags BridgeFieldFlags = BindingFlags.NonPublic | BindingFlags.Instance;

        // 清掉 ctx 对应 Actor 的所有临时预留；返回清掉数量
        public static int ClearFor(UnitRuntimeContext ctx)
        {
            if (ctx == null)
                return 0;

            // 1) 优先走 IOcc 适配器（推荐）
            var adapter = ctx.occService as HexOccServiceAdapter;
            if (adapter != null && adapter.backing != null && adapter.actorResolver != null)
            {
                var store = adapter.backing.Get();
                var actor = adapter.actorResolver.GetOrBind(ctx);
                return store != null && actor != null ? store.TempClearForOwner(actor) : 0;
            }

            // 2) 兜底：旧桥路径（万一还未注入 IOcc）
            var bridge = ctx.GetComponent<PlayerOccupancyBridge>();
            if (bridge != null)
            {
                var occField = typeof(PlayerOccupancyBridge).GetField("_occ", BridgeFieldFlags);
                var actorField = typeof(PlayerOccupancyBridge).GetField("_actor", BridgeFieldFlags);
                var occ = occField != null ? occField.GetValue(bridge) as HexOccupancy : null;
                var actor = actorField != null ? actorField.GetValue(bridge) as IGridActor : null;
                if (occ != null && actor != null)
                    return occ.TempClearForOwner(actor);
            }

            return 0;
        }

        // 预留一个 cell 给 ctx 所属的 Actor；用于路线高亮/拖尾
        public static bool Reserve(UnitRuntimeContext ctx, Hex cell)
        {
            if (ctx == null)
                return false;

            var adapter = ctx.occService as HexOccServiceAdapter;
            if (adapter != null && adapter.backing != null && adapter.actorResolver != null)
            {
                var store = adapter.backing.Get();
                var actor = adapter.actorResolver.GetOrBind(ctx);
                return store != null && actor != null && store.TempReserve(cell, actor);
            }

            // 兜底（极少用到）
            return false;
        }
    }
}
