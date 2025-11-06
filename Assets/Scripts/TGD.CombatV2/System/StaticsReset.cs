using UnityEngine;

namespace TGD.CombatV2
{
    public static class StaticsReset
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAll()
        {
            AttackEventsV2.Reset();
        }
    }
}
