using TGD.CombatV2.Integration;
using TGD.CoreV2;
using TGD.HexBoard;
using UnityEngine;

namespace TGD.CombatV2
{
    static class UnitRuntimeBindingUtil
    {
        public static Unit ResolveUnit(UnitRuntimeContext ctx, HexBoardTestDriver driver)
        {
            if (ctx != null && ctx.boundUnit != null)
                return ctx.boundUnit;

            return driver != null ? driver.UnitRef : null;
        }

        public static Transform ResolveUnitView(Component owner, UnitRuntimeContext ctx, HexBoardTestDriver driver, Transform viewOverride)
        {
            if (viewOverride)
                return viewOverride;

            var unit = ResolveUnit(ctx, driver);
            if (unit != null && UnitAnchorV2.TryGetView(unit, out var anchor))
                return anchor;

            if (driver != null && driver.unitView != null)
                return driver.unitView;

            if (ctx != null)
                return ctx.transform;

            return owner != null ? owner.transform : null;
        }

        public static IGridActor ResolveGridActor(Unit unit, HexOccupancy occupancy, IActorOccupancyBridge bridge)
        {
            if (bridge != null && bridge.Actor is IGridActor bridgedActor)
                return bridgedActor;

            if (occupancy != null)
            {
                if (bridge != null && bridge.IsReady && occupancy.TryGetActor(bridge.CurrentAnchor, out var bridgeOccActor) && bridgeOccActor != null)
                    return bridgeOccActor;

                if (unit != null && occupancy.TryGetActor(unit.Position, out var occActor) && occActor != null)
                    return occActor;
            }

            return null;
        }

        public static Hex ResolveAnchor(Unit unit, HexOccupancy occupancy, IActorOccupancyBridge bridge)
        {
            if (bridge != null && bridge.IsReady)
                return bridge.CurrentAnchor;

            if (bridge != null && bridge.Actor is IGridActor bridgedActor && !bridgedActor.Anchor.Equals(Hex.Zero))
                return bridgedActor.Anchor;

            if (occupancy != null)
            {
                if (bridge != null && occupancy.TryGetActor(bridge.CurrentAnchor, out var bridgeOccActor) && bridgeOccActor != null)
                    return bridgeOccActor.Anchor;

                if (unit != null && occupancy.TryGetActor(unit.Position, out var occActor) && occActor != null)
                    return occActor.Anchor;
            }

            return unit != null ? unit.Position : Hex.Zero;
        }

        public static void SyncUnit(Unit unit, Hex anchor, Facing4 facing)
        {
            if (unit == null)
                return;

            unit.Position = anchor;
            unit.Facing = facing;
        }

        public static PlayerOccupancyBridge ResolvePlayerBridge(Component owner, UnitRuntimeContext ctx, PlayerOccupancyBridge bridgeOverride, PlayerOccupancyBridge current)
        {
            if (bridgeOverride != null)
                return bridgeOverride;

            if (current != null && current)
                return current;

            if (ctx != null)
            {
                var candidate = ctx.GetComponent<PlayerOccupancyBridge>();
                if (candidate != null)
                    return candidate;

                candidate = ctx.GetComponentInChildren<PlayerOccupancyBridge>(true);
                if (candidate != null)
                    return candidate;

                candidate = ctx.GetComponentInParent<PlayerOccupancyBridge>(true);
                if (candidate != null)
                    return candidate;
            }

            if (owner != null)
            {
                var candidate = owner.GetComponent<PlayerOccupancyBridge>();
                if (candidate != null)
                    return candidate;
            }

            if (owner != null)
            {
                var candidate = owner.GetComponentInChildren<PlayerOccupancyBridge>(true);
                if (candidate != null)
                    return candidate;

                return owner.GetComponentInParent<PlayerOccupancyBridge>(true);
            }

            return null;
        }
    }
}
