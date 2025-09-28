using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    /// ��һ�� Hex ������ Tiler�����㷨/�������
    public sealed class HexAreaPainter
    {
        readonly HexBoardTiler tiler;
        readonly List<(GameObject go, Color old)> tinted = new();

        public HexAreaPainter(HexBoardTiler tiler) { this.tiler = tiler; }


        public void Paint(IEnumerable<Hex> cells, Color color)
        {
            
            if (tiler == null || cells == null) return;
            foreach (var h in cells)
                if (tiler.TryGetTile(h, out var go) && go) { var old = Color.white; Set(go, color); tinted.Add((go, old)); }

        }

        public void Clear()
        {
            foreach (var (go, old) in tinted) if (go) Set(go, Color.white);
            tinted.Clear();
        }

        static void Set(GameObject go, Color c)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", c);
                mpb.SetColor("_Color", c);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}