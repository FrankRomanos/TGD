// File: TGD.HexBoard/IStickyMoveSource.cs
namespace TGD.HexBoard
{
    public interface IStickyMoveSource
    {
        /// <summary>
        /// 在格子 at 是否提供贴附（百分比乘数 + 持续回合 + 去重tag）。
        /// - multiplier: 例如 0.8f、1.2f
        /// - durationTurns: <0=永久；>=0=按回合
        /// - tag: 同源去重Key（例如 "Patch@8,6"、"Hazard@AcidPool@14,3"）
        /// </summary>
        bool TryGetSticky(Hex at, out float multiplier, out int durationTurns, out string tag);
    }
}

