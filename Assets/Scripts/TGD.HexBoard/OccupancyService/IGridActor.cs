using TGD.CoreV2;
namespace TGD.HexBoard
{
    /// κΡλ䵽ӲҪСϢ
    public interface IGridActor
    {
        string Id { get; }
        Hex Anchor { get; set; }   // ռλê㣨
        Facing4 Facing { get; set; }   //  Q / R
        HitShape HitShape { get; } // 命中区域定义
    }
}
