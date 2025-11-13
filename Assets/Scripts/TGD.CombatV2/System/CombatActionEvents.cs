using System;

namespace TGD.CombatV2
{
    public static class CombatActionEvents
    {
        public static event Action<ActionContextV2> Resolved;

        public static void RaiseResolved(ActionContextV2 context)
        {
            Resolved?.Invoke(context);
        }
    }
}
