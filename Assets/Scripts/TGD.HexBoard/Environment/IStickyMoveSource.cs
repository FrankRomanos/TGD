// File: TGD.HexBoard/IStickyMoveSource.cs
namespace TGD.HexBoard
{
    /// 提供“进入某格时施加黏性移速（加速或减速）”的信息。
    /// 返回 multiplier (>1=加速, <1=减速), durationTurns（<0=永久，0=不附着）
    public interface IStickyMoveSource
    {
        bool TryGetSticky(Hex cell, out float multiplier, out int durationTurns);
    }
}

