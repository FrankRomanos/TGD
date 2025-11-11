// File: TGD.CombatV2/Integration/OccControllerReady.cs
using UnityEngine;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.CombatV2.Integration
{
    public static class OccControllerReady
    {
        public static bool Ensure(UnitRuntimeContext ctx, Component owner, out UnitGridAdapter actor, bool placeIfMissing = true)
        {
            actor = null;
            if (ctx == null || ctx.occService == null) return false;

            actor = owner.GetComponent<UnitGridAdapter>() ?? owner.GetComponentInChildren<UnitGridAdapter>(true);
            if (actor == null) return false;

            if (placeIfMissing)
                TryPlaceSimple(ctx, actor.Anchor, actor.Facing, owner, "Ensure");

            return true;
        }

        public static void Cleanup(UnitRuntimeContext ctx)
        {
            if (ctx == null || ctx.occService == null) return;
            // 仅清理软预留与未提交 token；不要调用 Remove（那是“把自己从棋盘移除”）
            TGD.HexBoard.OccTempOps.ClearFor(ctx);

            // 如果你的 CancelAll 也是事务型(… out OccTxnId)，放开下两行并替换签名即可；
            // 否则保留老的无 out 版本：
            // OccTxnId cancelTxn;
            // ctx.occService.CancelAll(ctx, out cancelTxn);

            ctx.occService.CancelAll(ctx);
        }

        public static bool TryPlaceSimple(UnitRuntimeContext ctx, Hex anchor, Facing4 facing, Component owner = null, string tag = null)
        {
            if (ctx == null || ctx.occService == null) return false;
            OccTxnId txn; OccFailReason reason;
            bool ok = ctx.occService.TryPlace(ctx, anchor, facing, out txn, out reason);
            if (!ok && owner != null)
                Debug.LogWarning($"[IOCC] TryPlace fail{Fmt(tag)} anchor={anchor} reason={reason} txn={txn}", owner);
            return ok;
        }

        public static bool TryMoveSimple(UnitRuntimeContext ctx, Hex anchor, Facing4 facing, Component owner = null, string tag = null)
        {
            if (ctx == null || ctx.occService == null) return false;
            OccTxnId txn; OccFailReason reason;
            bool ok = ctx.occService.TryMove(ctx, anchor, facing, out txn, out reason);
            if (!ok && owner != null)
                Debug.LogWarning($"[IOCC] TryMove fail{Fmt(tag)} anchor={anchor} reason={reason} txn={txn}", owner);
            return ok;
        }

        // ★ 事务型 Remove（把自己从棋盘移除，用在 OnDisable/销毁时）
        public static void RemoveSimple(UnitRuntimeContext ctx, Component owner = null, string tag = null)
        {
            if (ctx == null || ctx.occService == null) return;
            OccTxnId txn;
            ctx.occService.Remove(ctx, out txn);
            // 如需日志：
            // if (owner != null) Debug.Log($"[IOCC] Remove{(string.IsNullOrEmpty(tag)?"":$"({tag})")} txn={txn}", owner);
        }
        static string Fmt(string tag) => string.IsNullOrEmpty(tag) ? "" : $"({tag})";
    }
}
