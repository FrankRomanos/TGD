using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    public sealed class HexAreaPainter : IHexHighlighter
    {
        readonly HexBoardTiler tiler;
        readonly HashSet<Renderer> _tinted = new();

        public HexAreaPainter(HexBoardTiler tiler)
        {
            this.tiler = tiler;
        }

        public void Paint(IEnumerable<Hex> cells, Color color, int priority = 0)
        {
            if (tiler == null || cells == null)
                return;

            foreach (var h in cells)
            {
                if (!tiler.TryGetTile(h, out var go) || !go)
                    continue;

                var renderers = go.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (!renderer)
                        continue;

                    _tinted.Add(renderer);
                    HexTileTintRegistry.Apply(renderer, this, color, priority);
                }
            }
        }

        public void Clear()
        {
            if (_tinted.Count == 0)
                return;

            foreach (var renderer in _tinted)
            {
                if (!renderer)
                    continue;
                HexTileTintRegistry.Remove(renderer, this);
            }

            _tinted.Clear();
        }
    }
}
