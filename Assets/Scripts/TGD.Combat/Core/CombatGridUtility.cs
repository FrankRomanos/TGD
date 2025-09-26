using TGD.Grid;

namespace TGD.Combat
{
    public static class CombatGridUtility
    {
        public static HexCoord Resolve(Unit unit, HexGridMap<Unit> map)
        {
            if (unit == null)
                return HexCoord.Zero;
            if (map != null && map.TryGetPosition(unit, out var coord))
                return coord;
            return unit.Position;
        }

        public static bool TryResolve(Unit unit, HexGridMap<Unit> map, out HexCoord coord)
        {
            if (unit != null && map != null && map.TryGetPosition(unit, out coord))
                return true;
            if (unit != null)
            {
                coord = unit.Position;
                return true;
            }
            coord = default;
            return false;
        }
    }
}
