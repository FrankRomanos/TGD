using System.Collections.Generic;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.HexBoard.Pathfinding
{
    public interface IPathBlocker
    {
        bool IsBlocked(Hex h);
    }

    public sealed class StartFootprintPassBlocker : IPathBlocker
    {
        readonly HexOccupancy _occ;
        readonly HashSet<Hex> _startCells = new();

        public StartFootprintPassBlocker(HexOccupancy occ, IGridActor self)
        {
            _occ = occ;
            if (occ == null || self == null)
                return;

            var cells = occ.CellsOf(self);
            if (cells != null && cells.Count > 0)
            {
                foreach (var cell in cells)
                    _startCells.Add(cell);
                return;
            }

            var anchor = self.Anchor;
            if (self.Footprint != null)
            {
                foreach (var cell in HexFootprint.Expand(anchor, self.Facing, self.Footprint))
                    _startCells.Add(cell);
            }
            else
            {
                _startCells.Add(anchor);
            }
        }

        public bool IsBlocked(Hex h)
        {
            if (_startCells.Contains(h))
                return false;
            if (_occ == null)
                return true;
            return _occ.IsBlockedFormal(h);
        }
    }
}
