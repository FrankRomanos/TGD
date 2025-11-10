using UnityEngine;
using TGD.HexBoard;

namespace TGD.CoreV2
{
    public static class OccDiagnostics
    {
        public static bool TraceLog = true;

        static IOccupancyService _canonicalService;
        static string _canonicalBoardId;

        public static void Log(OccAction act, OccTxnId id, string actorId, Hex from, Hex to, OccFailReason reason)
        {
            if (!TraceLog) return;
            Debug.Log("[Occ] " + act + " tx=" + id + " actor=" + actorId + " " + from + "->" + to + " reason=" + reason);
        }

        public static void AssertSingleStore(IOccupancyService occ, string where)
        {
            if (occ == null)
            {
                Debug.LogError("[Occ] No IOccupancyService @" + where);
                return;
            }

            if (_canonicalService == null)
            {
                _canonicalService = occ;
                _canonicalBoardId = occ.BoardId;
                return;
            }

            if (!ReferenceEquals(_canonicalService, occ))
            {
                var canonicalBoard = string.IsNullOrEmpty(_canonicalBoardId) ? "<null>" : _canonicalBoardId;
                var board = string.IsNullOrEmpty(occ.BoardId) ? "<null>" : occ.BoardId;
                Debug.LogError($"[Occ] Multiple IOccupancyService detected @ {where}: canonical={canonicalBoard}, incoming={board}.");
            }
        }

        public static void RunHealthCheck()
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
