// File: TGD.HexBoard/HexHitUtil.cs
using System.Collections.Generic;
using TGD.CoreV2;

namespace TGD.HexBoard
{
    public static class HexHitUtil
    {
        // 命中：footprint 与 hitCells 交集非空
        public static bool Hit(IGridActor actor, HashSet<Hex> hitCells)
        {
            if (actor == null || hitCells == null) return false;
            foreach (var c in HexFootprint.Expand(actor.Anchor, actor.Facing, actor.Footprint))
                if (hitCells.Contains(c)) return true;
            return false;
        }

        // 线段加粗（A->B，宽度=T）
        public static HashSet<Hex> ThickLine(Hex a, Hex b, int T)
        {
            var set = new HashSet<Hex>();
            foreach (var h in Hex.Line(a, b))
            {
                if (T <= 0) { set.Add(h); continue; }
                foreach (var t in Hex.Range(h, T)) set.Add(t);
            }
            return set;
        }
    }
}
