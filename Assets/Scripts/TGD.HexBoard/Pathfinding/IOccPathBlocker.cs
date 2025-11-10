using System.Collections.Generic;
using TGD.CoreV2;

namespace TGD.HexBoard.Pathfinding
{
    /// 基于 IOcc 的可走性拦截器：允许自身起步格通过，其它格用 IOcc 判“是否可走”
    public sealed class IOccPathBlocker : IPathBlocker
    {
        readonly UnitRuntimeContext _ctx;
        readonly Facing4 _face;
        readonly HashSet<Hex> _startCells = new HashSet<Hex>();

        public IOccPathBlocker(UnitRuntimeContext ctx, IGridActor self)
        {
            _ctx = ctx;
            _face = (self != null) ? self.Facing : Facing4.PlusQ;

            if (self != null)
            {
                var anchor = self.Anchor;
                var fp = self.Footprint;
                if (fp != null)
                {
                    foreach (var c in HexFootprint.Expand(anchor, self.Facing, fp))
                        _startCells.Add(c);
                }
                else
                {
                    _startCells.Add(anchor);
                }
            }
        }

        public bool IsBlocked(Hex h)
        {
            // 自己脚下的格子永远允许起步
            if (_startCells.Contains(h))
                return false;

            // 没有上下文/服务就保守视为阻挡，避免穿模
            if (_ctx == null || _ctx.occService == null)
                return true;

            // IOcc 返回“是否空闲”，拦截器需要“是否阻挡”
            bool free = _ctx.occService.IsFreeFor(_ctx, h, _face);
            return !free;
        }
    }
}
