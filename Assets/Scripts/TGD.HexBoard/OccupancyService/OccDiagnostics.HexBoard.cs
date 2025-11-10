using UnityEngine;

namespace TGD.CoreV2
{
    public static partial class OccDiagnostics
    {
        static partial void RunHealthCheckPlatform()
        {
            var contexts = Object.FindObjectsByType<UnitRuntimeContext>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < contexts.Length; i++)
            {
                var ctx = contexts[i];
                var adapters = ctx.GetComponentsInChildren<TGD.HexBoard.UnitGridAdapter>(true);
                if (adapters.Length > 1)
                {
                    Debug.LogError($"[OccCheck] {ctx.name} has {adapters.Length} UnitGridAdapter! This can break 'ignore-self' logic. Remove extras.", ctx);
                }
            }
        }
    }
}
