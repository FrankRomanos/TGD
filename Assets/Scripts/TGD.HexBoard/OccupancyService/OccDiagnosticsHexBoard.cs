using UnityEngine;
using TGD.CoreV2;

namespace TGD.HexBoard
{
    static class OccDiagnosticsHexBoard
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            OccDiagnostics.RegisterHealthCheck(RunHealthCheck);
        }

        static void RunHealthCheck()
        {
            var contexts = Object.FindObjectsByType<UnitRuntimeContext>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < contexts.Length; i++)
            {
                var ctx = contexts[i];
                var adapters = ctx.GetComponentsInChildren<UnitGridAdapter>(true);
                if (adapters.Length > 1)
                {
                    Debug.LogError($"[OccCheck] {ctx.name} has {adapters.Length} UnitGridAdapter! This can break 'ignore-self' logic. Remove extras.", ctx);
                }
            }
        }
    }
}
