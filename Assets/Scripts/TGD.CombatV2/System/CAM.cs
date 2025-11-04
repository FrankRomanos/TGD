using TGD.CoreV2;

namespace TGD.CombatV2
{
    public static class CAM
    {
        public static event System.Action<UnitRuntimeContext, string> ActionResolved;

        internal static void RaiseActionResolved(UnitRuntimeContext context, string actionId)
            => ActionResolved?.Invoke(context, actionId);
    }
}
