using System.Collections.Generic;
using UnityEngine;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.CombatV2.Targeting
{
    /// <summary>
    /// Shared helper for temporary target previews (range = blue, hover = yellow/red).
    /// </summary>
    public sealed class TargetSelectionCursor
    {
        const int BasePriority = HexTileTintPriority.TargetSelection;

        readonly IHexHighlighter _highlighter;
        readonly List<Hex> _last = new();
        readonly List<Hex> _singleBuffer = new(1);

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
                _highlighter.Paint(_last, color, BasePriority);
        }

        public void ShowSingle(Hex hex, Color color)
        {
            if (_highlighter == null)
                return;

            _last.Clear();
            _last.Add(hex);
            _highlighter.Clear();
            _highlighter.Paint(_last, color, BasePriority + 1);
        }

        public void ShowArea(
            IReadOnlyList<Hex> valid,
            IReadOnlyList<Hex> invalid,
            Hex? hover,
            bool hoverValid,
            Color rangeColor,
            Color invalidColor,
            Color hoverValidColor,
            Color hoverInvalidColor)
        {
            if (_highlighter == null)
                return;

            _last.Clear();
            if (valid != null)
                _last.AddRange(valid);
            if (invalid != null)
                _last.AddRange(invalid);

            _highlighter.Clear();

            if (_last.Count > 0)
                _highlighter.Paint(_last, rangeColor, BasePriority);

            if (hover.HasValue)
            {
                _singleBuffer.Clear();
                _singleBuffer.Add(hover.Value);
                var hoverColor = hoverValid ? hoverValidColor : hoverInvalidColor;
                _highlighter.Paint(_singleBuffer, hoverColor, BasePriority + 1);

                // Track hover in the cache so Clear() removes it next frame.
                if (!_last.Contains(hover.Value))
                    _last.Add(hover.Value);
            }
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
