using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.CoreV2
{
    public static class OccDiagnostics
    {
        public static bool TraceLog = true;

        static IOccupancyService _canonicalService;
        static string _canonicalBoardId;
        static Action _healthCheckHandler;
        static Func<OccTokenLedgerSnapshot> _tokenSnapshotProvider;

        public static void Log(OccAction act, OccTxnId id, string actorId, Hex from, Hex to, OccFailReason reason)
        {
            if (!TraceLog) return;
            Debug.Log("[Occ] " + act + " tx=" + id + " actor=" + actorId + " " + from + "->" + to + " reason=" + reason);
        }

        public static void LogReserve(OccToken token, string actorId, int cellCount)
        {
            if (!TraceLog) return;
            Debug.Log($"[Occ] Reserve token={token} owner={actorId} cells={cellCount}");
        }

        public static void LogCommit(OccToken token, Hex finalAnchor, OccFailReason reason)
        {
            if (!TraceLog) return;
            Debug.Log($"[Occ] Commit token={token} -> {finalAnchor} reason={reason}");
        }

        public static void LogCancel(OccToken token, string reason)
        {
            if (!TraceLog) return;
            Debug.Log($"[Occ] Cancel token={token} reason={reason ?? "Manual"}");
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

        public static void RegisterHealthCheck(Action handler)
        {
            _healthCheckHandler = handler;
        }

        public static void RunHealthCheck()
        {
            _healthCheckHandler?.Invoke();
        }

        public static void RegisterTokenSnapshotProvider(Func<OccTokenLedgerSnapshot> provider)
        {
            _tokenSnapshotProvider = provider;
        }

        public static OccTokenLedgerSnapshot CaptureTokenSnapshot()
        {
            return _tokenSnapshotProvider?.Invoke() ?? OccTokenLedgerSnapshot.Empty;
        }

        public readonly struct OccTokenLedgerSnapshot
        {
            public static readonly OccTokenLedgerSnapshot Empty = new OccTokenLedgerSnapshot(0, Array.Empty<OccTokenOwnerSnapshot>());

            public readonly int ActiveTokens;
            public readonly IReadOnlyList<OccTokenOwnerSnapshot> Owners;

            public OccTokenLedgerSnapshot(int activeTokens, IReadOnlyList<OccTokenOwnerSnapshot> owners)
            {
                ActiveTokens = activeTokens;
                Owners = owners ?? Array.Empty<OccTokenOwnerSnapshot>();
            }
        }

        public readonly struct OccTokenOwnerSnapshot
        {
            public readonly string ActorId;
            public readonly int TokenCount;

            public OccTokenOwnerSnapshot(string actorId, int tokenCount)
            {
                ActorId = actorId;
                TokenCount = tokenCount;
            }
        }
    }
}
