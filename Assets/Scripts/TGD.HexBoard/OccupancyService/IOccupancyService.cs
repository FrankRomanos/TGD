using TGD.CoreV2;

namespace TGD.HexBoard
{
    public interface IOccupancyService
    {
        bool TryPlace(UnitRuntimeContext ctx, Hex anchor, Facing4 facing);
        bool TryMove(UnitRuntimeContext ctx, Hex anchor, Facing4 facing);
        void Remove(UnitRuntimeContext ctx);
        bool IsFree(Hex anchor, FootprintShape fp, Facing4 facing);
        bool TryGetActor(Hex anchor, out IGridActor actor);
    }
}
