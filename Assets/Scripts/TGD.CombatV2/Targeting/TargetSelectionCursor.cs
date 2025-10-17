using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2.Targeting
{
    /// <summary>
    /// Shared helper for temporary target previews (yellow = valid, red = invalid).
    /// </summary>
    public sealed class TargetSelectionCursor
    {
        readonly HexAreaPainter _painter;

        public TargetSelectionCursor(HexBoardTiler tiler)
        {
            if (tiler != null)
            {
                tiler.EnsureBuilt();
                _painter = new HexAreaPainter(tiler);
            }
        }

        public void ShowPath(IEnumerable<Hex> cells, Color color)
        {
            if (_painter == null)
                return;

            _painter.Clear();
            if (cells == null)
                return;

            _painter.Paint(cells, color);
        }

        public void ShowSingle(Hex hex, Color color)
        {
            if (_painter == null)
                return;

            _painter.Clear();
            _painter.Paint(new[] { hex }, color);
        }

        public void Clear()
        {
            _painter?.Clear();
        }
    }
}
