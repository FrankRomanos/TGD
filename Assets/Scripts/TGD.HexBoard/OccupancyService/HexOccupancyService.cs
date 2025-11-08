using UnityEngine;
using TGD.CoreV2;

namespace TGD.HexBoard
{
    [DefaultExecutionOrder(-1000)]
    public sealed class HexOccupancyService : MonoBehaviour, IOccupancyService
    {
        public HexBoardAuthoringLite authoring;
        HexOccupancy _occ;

        public HexOccupancy Get()
        {
            if (_occ == null && authoring != null && authoring.Layout != null)
                _occ = new HexOccupancy(authoring.Layout);
            return _occ;
        }

        public bool Register(IGridActor actor, Hex anchor, Facing4 facing)
            => Get() != null && Get().TryPlace(actor, anchor, facing);

        public void Unregister(IGridActor actor) { Get()?.Remove(actor); }

        IGridActor EnsureAdapter(UnitRuntimeContext ctx)
        {
            if (ctx == null)
                return null;

            var adapter = ctx.GetComponent<UnitGridAdapter>()
                          ?? ctx.GetComponentInChildren<UnitGridAdapter>(true);

            if (adapter == null)
                adapter = ctx.gameObject.AddComponent<UnitGridAdapter>();

            if (adapter.Unit == null && ctx.boundUnit != null)
                adapter.Unit = ctx.boundUnit;

            if (ctx.occService == null)
                ctx.occService = this;

            return adapter;
        }

        string ResolveLabel(UnitRuntimeContext ctx, IGridActor actor)
        {
            if (ctx != null && ctx.boundUnit != null && !string.IsNullOrEmpty(ctx.boundUnit.Id))
                return ctx.boundUnit.Id;

            if (actor != null && !string.IsNullOrEmpty(actor.Id))
                return actor.Id;

            return ctx != null ? ctx.name : name;
        }

        public bool TryPlace(UnitRuntimeContext ctx, Hex anchor, Facing4 facing)
        {
            var occ = Get();
            var actor = EnsureAdapter(ctx);
            if (occ == null || actor == null)
                return false;

            bool placed = occ.TryPlace(actor, anchor, facing);
            if (placed)
            {
                actor.Anchor = anchor;
                actor.Facing = facing;
                Debug.Log($"[OCC] Place {ResolveLabel(ctx, actor)} @ {anchor} via IOccupancyService={GetType().Name}", this);
            }
            return placed;
        }

        public bool TryMove(UnitRuntimeContext ctx, Hex anchor, Facing4 facing)
        {
            var occ = Get();
            var actor = EnsureAdapter(ctx);
            if (occ == null || actor == null)
                return false;

            bool moved = occ.TryMove(actor, anchor);
            if (!moved)
                moved = occ.TryPlace(actor, anchor, facing);

            if (moved)
            {
                actor.Anchor = anchor;
                actor.Facing = facing;
            }

            return moved;
        }

        public void Remove(UnitRuntimeContext ctx)
        {
            if (ctx == null)
                return;

            var adapter = ctx.GetComponent<UnitGridAdapter>()
                          ?? ctx.GetComponentInChildren<UnitGridAdapter>(true);
            if (adapter != null)
                Get()?.Remove(adapter);
        }

        public bool IsFree(Hex anchor, FootprintShape fp, Facing4 facing)
        {
            var occ = Get();
            return occ != null && occ.IsFree(anchor, fp, facing);
        }

        public bool TryGetActor(Hex anchor, out IGridActor actor)
        {
            var occ = Get();
            if (occ != null)
                return occ.TryGetActor(anchor, out actor);

            actor = null;
            return false;
        }
    }
}
