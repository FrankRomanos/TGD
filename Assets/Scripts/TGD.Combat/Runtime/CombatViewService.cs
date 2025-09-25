using System.Collections.Generic;
using TGD.Grid;


namespace TGD.Combat
{
    /// <summary>
    /// 提供 CombatLoop 与场景视图的解耦查询接口。
    /// </summary>
    public interface ICombatViewProbe
    {
        bool TryResolveUnitCoordinate(Unit unit, HexGridLayout referenceLayout, out HexCoord coord);
        IEnumerable<HexGridAuthoring> EnumerateKnownGrids();
    }

    public static class CombatViewServices
    {
        /// <summary>
        /// 由视图层在运行时注册，可为空。
        /// </summary>
        public static ICombatViewProbe SceneProbe { get; set; }
    }
}