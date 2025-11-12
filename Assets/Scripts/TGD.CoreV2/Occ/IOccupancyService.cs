using System.Collections.Generic;

namespace TGD.CoreV2
{
    /// <summary>
    /// Read-only view describing who currently occupies a cell.
    /// Kept intentionally small so gameplay systems can diagnose conflicts
    /// without ever touching the underlying HexBoard implementation details.
    /// </summary>
    public sealed class OccActorInfo
    {
        public string ActorId;
        public Hex Anchor;
        public Facing4 Facing;
        public string FootprintKey; // Diagnostics only; not for gameplay logic.
        public OccActorInfo(string id, Hex a, Facing4 f, string key)
        { ActorId = id; Anchor = a; Facing = f; FootprintKey = key; }
    }

    /// <summary>
    /// Handle returned from <see cref="ReservePath"/>.  The token is required when
    /// promoting a soft reservation into a committed write, or when a caller wants
    /// to explicitly cancel all temporary claims they previously issued.
    /// Tokens are lightweight structs so they can safely cross animation, camera,
    /// or UI boundaries without allocations.
    /// </summary>
    public readonly struct OccToken
    {
        public readonly ulong id;
        public OccToken(ulong i) { id = i; }
        public bool IsValid => id != 0;
        public override string ToString() => id.ToString();
    }

    /// <summary>
    /// High-level result returned by <see cref="ReservePath"/> to explain why a
    /// movement preview failed.  Keep this enum synced with diagnostics logs so
    /// designers can track down "ghost" blocks in the editor.
    /// </summary>
    public enum OccReserveResult
    {
        Ok = 0,
        AlreadyReserved,
        Blocked,
        NoStore,
        NoActor
    }

    /// <summary>
    /// Describes how aggressive a path reservation should be.
    /// <list type="bullet">
    /// <item><description><see cref="SoftPath"/> holds cells as hints only and allows overlaps.</description></item>
    /// <item><description><see cref="EndOnlyHard"/> pins just the destination so melee slides feel firm.</description></item>
    /// <item><description><see cref="PathHard"/> locks every step for formations or scripted set pieces.</description></item>
    /// </list>
    /// </summary>
    public enum OccReserveMode
    {
        SoftPath = 0,
        EndOnlyHard = 1,
        PathHard = 2
    }

    /// <summary>
    /// IOccupancyService is the single authoritative write gateway to the battle
    /// grid.  Controllers, factories, and movers must never mutate HexOccupancy
    /// directly — instead they reserve intent, commit once animation confirms, and
    /// cancel whenever the interaction aborts.  The service resolves UnitRuntimeContext
    /// into concrete grid actors, tracks temporary reservations, and emits
    /// OccTxnId values for audit logs.
    /// </summary>
    public interface IOccupancyService
    {
        /// <summary>
        /// Atomically places a freshly spawned unit at the requested anchor.
        /// The service decodes ctx → UnitGridAdapter so callers do not need
        /// to cache adapters during factory wiring.  Returns a transaction id
        /// so TMV2/CAMV2 logs can correlate placement with action timelines.
        /// </summary>
        bool TryPlace(UnitRuntimeContext ctx, Hex anchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason);

        /// <summary>
        /// Moves an already placed actor to a new anchor.  Fails fast if the
        /// context was never registered or if the destination is formally blocked.
        /// </summary>
        bool TryMove(UnitRuntimeContext ctx, Hex anchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason);

        /// <summary>
        /// Forcefully removes the actor from the store (e.g. on death or factory
        /// despawn).  The service also clears outstanding reservations so movers
        /// cannot leak stale tokens.
        /// </summary>
        void Remove(UnitRuntimeContext ctx, out OccTxnId txn);

        /// <summary>
        /// Creates a temporary claim along a path so UI previews and animation
        /// blockers can show intent.  Returns a token the caller must keep for
        /// <see cref="Commit"/> or <see cref="Cancel"/>.
        /// </summary>
        bool ReservePath(UnitRuntimeContext ctx, IReadOnlyList<Hex> cells, out OccToken token, out OccReserveResult result,
            OccReserveMode mode = OccReserveMode.SoftPath);

        /// <summary>
        /// Promotes a prior reservation into a hard write.  This is the only
        /// method that actually updates the HexOccupancy store after W2 confirms
        /// the action, keeping Commit semantics aligned with CAMV2 reporting.
        /// </summary>
        bool Commit(UnitRuntimeContext ctx, OccToken token, Hex finalAnchor, Facing4 facing, out OccTxnId txn, out OccFailReason reason);

        /// <summary>
        /// Releases a single reservation token.  Safe to call multiple times and
        /// used heavily by controllers on Disable/Cancel transitions.
        /// </summary>
        bool Cancel(UnitRuntimeContext ctx, OccToken token, string reason = null);

        /// <summary>
        /// Bulk-cancels every reservation issued by a context.  Typically called
        /// when CAMV2 aborts an action tree or a unit despawns mid-animation.
        /// </summary>
        int CancelAll(UnitRuntimeContext ctx, string reason = null);

        /// <summary>
        /// Lightweight read path for “can I stand there?” checks.  Uses the same
        /// adapter decoding logic to ensure factories and controllers stay in sync
        /// with the authoritative footprint rules.
        /// </summary>
        bool IsFreeFor(UnitRuntimeContext ctx, Hex anchor, Facing4 facing);

        /// <summary>
        /// Debug helper returning a minimal view of who currently occupies a cell.
        /// Only intended for tooling, logging, or panic UI overlays.
        /// </summary>
        bool TryGetActorInfo(Hex anchor, out OccActorInfo info);

        /// <summary>
        /// Snapshot the entire occupancy store.  Primarily used by diagnostics and
        /// future replay / rollback tooling.
        /// </summary>
        OccSnapshot[] DumpAll();

        /// <summary>
        /// Monotonically increasing number that increments every time the backing
        /// store mutates.  UI systems can poll this to detect when they need to
        /// rebuild cached highlights.
        /// </summary>
        int StoreVersion { get; }

        /// <summary>
        /// Identifier describing which board this store maps to.  Useful when we
        /// serialize dumps across scenes or compare diagnostics between instances.
        /// </summary>
        string BoardId { get; }
    }
}
