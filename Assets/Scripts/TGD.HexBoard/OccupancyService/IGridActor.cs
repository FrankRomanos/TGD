using TGD.CoreV2;
namespace TGD.HexBoard
{
    /// 把任何“单位”适配到格子层需要的最小信息
    public interface IGridActor
    {
        string Id { get; }
        Hex Anchor { get; set; }   // 占位锚点（整数格）
        Facing4 Facing { get; set; }   // 仅 ±Q / ±R
        FootprintShape Footprint { get; } // 规则占位（SO）
    }
}
