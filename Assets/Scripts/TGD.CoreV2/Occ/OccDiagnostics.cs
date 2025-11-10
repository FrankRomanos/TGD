using UnityEngine;

namespace TGD.CoreV2
{
    public static class OccDiagnostics
    {
        public static bool TraceLog = true;
        public static void Log(OccAction act, OccTxnId id, string actorId, Hex from, Hex to, OccFailReason reason)
        {
            if (!TraceLog) return;
            Debug.Log("[Occ] " + act + " tx=" + id + " actor=" + actorId + " " + from + "->" + to + " reason=" + reason);
        }

        public static void AssertSingleStore(IOccupancyService occ, string where)
        {
            if (occ == null) Debug.LogError("[Occ] No IOccupancyService @" + where);
        }
    }
}
