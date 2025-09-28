// File: TGD.HexBoard/HexOccupancy.cs
using System.Collections.Generic;

namespace TGD.HexBoard
{
    /// 负责：多格占位注册/释放、能否放置、尝试移动；独立于具体 Unit
    public sealed class HexOccupancy
    {
        public HexBoardLayout Layout { get; }
        readonly Dictionary<Hex, IGridActor> cellToActor = new();
        readonly Dictionary<IGridActor, List<Hex>> actorToCells = new();

        public HexOccupancy(HexBoardLayout layout) { Layout = layout; }

        public IReadOnlyList<Hex> CellsOf(IGridActor a) =>
            actorToCells.TryGetValue(a, out var list) ? list : System.Array.Empty<Hex>();

        public IGridActor Get(Hex c) => cellToActor.TryGetValue(c, out var a) ? a : null;
        public bool IsBlocked(Hex c, IGridActor ignore = null) =>
            cellToActor.TryGetValue(c, out var a) && a != null && a != ignore;

        public bool CanPlace(IGridActor a, Hex anchor, Facing4 facing, IGridActor ignore = null)
        {
            if (a?.Footprint == null) return Layout.Contains(anchor) && !IsBlocked(anchor, ignore);
            foreach (var c in HexFootprint.Expand(anchor, facing, a.Footprint))
            {
                if (!Layout.Contains(c)) return false;
                if (IsBlocked(c, ignore)) return false;
            }
            return true;
        }

        public bool TryPlace(IGridActor a, Hex anchor, Facing4 facing)
        {
            if (!CanPlace(a, anchor, facing)) return false;
            Remove(a);

            var cells = new List<Hex>();
            foreach (var c in HexFootprint.Expand(anchor, facing, a.Footprint))
            { cellToActor[c] = a; cells.Add(c); }
            actorToCells[a] = cells;

            a.Anchor = anchor; a.Facing = facing;
            return true;
        }

        public bool TryMove(IGridActor a, Hex nextAnchor) => TryPlace(a, nextAnchor, a.Facing);

        public void Remove(IGridActor a)
        {
            if (a == null) return;
            if (!actorToCells.TryGetValue(a, out var cells)) return;
            foreach (var c in cells)
                if (cellToActor.TryGetValue(c, out var who) && who == a) cellToActor.Remove(c);
            actorToCells.Remove(a);
        }
    }
}
