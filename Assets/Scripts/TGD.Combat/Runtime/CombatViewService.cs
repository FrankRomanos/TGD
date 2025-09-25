using System.Collections.Generic;
using TGD.Grid;


namespace TGD.Combat
{
    /// <summary>
    /// �ṩ CombatLoop �볡����ͼ�Ľ����ѯ�ӿڡ�
    /// </summary>
    public interface ICombatViewProbe
    {
        bool TryResolveUnitCoordinate(Unit unit, HexGridLayout referenceLayout, out HexCoord coord);
        IEnumerable<HexGridAuthoring> EnumerateKnownGrids();
    }

    public static class CombatViewServices
    {
        /// <summary>
        /// ����ͼ��������ʱע�ᣬ��Ϊ�ա�
        /// </summary>
        public static ICombatViewProbe SceneProbe { get; set; }
    }
}