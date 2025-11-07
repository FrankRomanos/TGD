using TGD.CoreV2;

namespace TGD.CombatV2
{
    public static class CAM
    {
        public static event System.Action<UnitRuntimeContext, string> ActionResolved;
        public static event System.Action<UnitRuntimeContext, string> ActionCancelled;

        internal static void RaiseActionResolved(UnitRuntimeContext context, string skillId)
            => ActionResolved?.Invoke(context, skillId);

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        static void LogCancel(UnitRuntimeContext ctx, string skillId, string reason)
        {
            ActionPhaseLogger.Log($"[Rules] Cancel skillId={skillId} ({reason})");
        }

        internal static void RaiseActionCancelled(UnitRuntimeContext ctx, string skillId, string reason = null)
        {
            LogCancel(ctx, skillId, reason ?? "unknown");
            try { ActionCancelled?.Invoke(ctx, skillId ?? string.Empty); }
            catch { /* 避免监听器异常影响主流程 */ }
        }
    }
}
