using TGD.CoreV2;

namespace TGD.CombatV2
{
    public static class CAM
    {
        public static event System.Action<UnitRuntimeContext, string> ActionResolved;
        public static event System.Action<UnitRuntimeContext, string> ActionCancelled;

        internal static void RaiseActionResolved(UnitRuntimeContext context, string actionId)
            => ActionResolved?.Invoke(context, actionId);

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        static void LogCancel(UnitRuntimeContext ctx, string actionId, string reason)
        {
            ActionPhaseLogger.Log($"[Rules] Cancel actionId={actionId} ({reason})");
        }

        internal static void RaiseActionCancelled(UnitRuntimeContext ctx, string actionId, string reason = null)
        {
            LogCancel(ctx, actionId, reason ?? "unknown");
            try { ActionCancelled?.Invoke(ctx, actionId ?? string.Empty); }
            catch { /* 避免监听器异常影响主流程 */ }
        }
    }
}
