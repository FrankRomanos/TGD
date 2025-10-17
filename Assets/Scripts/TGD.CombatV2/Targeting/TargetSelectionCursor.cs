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
        readonly IHexHighlighter _highlighter;
        readonly List<Hex> _last = new();

        public TargetSelectionCursor(IHexHighlighter highlighter)
        {
            _highlighter = highlighter;
        }

        public void ShowPath(IEnumerable<Hex> cells, Color color)
        {
            if (_highlighter == null)
                return;

            _last.Clear();
            if (cells != null)
                _last.AddRange(cells);

            _highlighter.Clear();
            if (_last.Count > 0)
                _highlighter.Paint(_last, color);
        }

        public void ShowSingle(Hex hex, Color color)
        {
            if (_highlighter == null)
                return;

            _last.Clear();
            _last.Add(hex);
            _highlighter.Clear();
            _highlighter.Paint(_last, color);
        }

        public void Clear()
        {
            if (_highlighter == null)
                return;

            _last.Clear();
            _highlighter.Clear();
        }
    }
}
