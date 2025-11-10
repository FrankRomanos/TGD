using System.Linq;
using UnityEngine;
using TGD.CoreV2;
using TGD.HexBoard;

public sealed class OccDebugHotkey : MonoBehaviour
{
    [SerializeField] KeyCode key = KeyCode.F9;

    void Update()
    {
        if (!Input.GetKeyDown(key)) return;

        var occ = FindFirstObjectByType<HexOccServiceAdapter>(FindObjectsInactive.Include);
        var store = occ?.backing?.Get();
        var ctxs = FindObjectsByType<UnitRuntimeContext>(FindObjectsSortMode.None);

        Debug.Log($"[OccCheck] board={occ?.BoardId ?? "<null>"} store={(store != null ? store.GetHashCode() : 0)}");
        foreach (var c in ctxs)
        {
            var ok = c.occService != null && ReferenceEquals(c.occService, occ);
            Debug.Log($"[OccCheck] ctx={c.name} occInjected={ok}");
        }

        var services = FindObjectsByType<HexOccupancyService>(FindObjectsSortMode.None);
        var uniqueStores = services.Select(s => s.Get()).Where(x => x != null).Distinct().Count();
        Debug.Log($"[OccCheck] HexOccupancyService={services.Length}, UniqueStores={uniqueStores}");

        var snapshot = OccDiagnostics.CaptureTokenSnapshot();
        Debug.Log($"[OccCheck] ActiveTokens={snapshot.ActiveTokens}");
        if (snapshot.Owners != null)
        {
            foreach (var owner in snapshot.Owners)
            {
                if (owner.TokenCount > 1)
                    Debug.LogWarning($"[OccCheck] Actor={owner.ActorId} tokens={owner.TokenCount}");
            }
        }
    }
}
