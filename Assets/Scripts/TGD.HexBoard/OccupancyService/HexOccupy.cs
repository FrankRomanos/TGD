// File: TGD.HexBoard/HexOccupancy.cs
using System.Collections.Generic;

namespace TGD.HexBoard
{
    public enum OccLayer
    {
        Normal = 0,
        TempAttack = 1
    }

    public sealed class HexOccupancy
    {
        public HexBoardLayout Layout { get; }
        readonly Dictionary<Hex, IGridActor> cellToActor = new();
        readonly Dictionary<IGridActor, List<Hex>> actorToCells = new();
        readonly Dictionary<Hex, IGridActor> tempCellToActor = new();
        readonly Dictionary<IGridActor, HashSet<Hex>> tempActorToCells = new();

        public HexOccupancy(HexBoardLayout layout) { Layout = layout; }

        public IReadOnlyList<Hex> CellsOf(IGridActor a) =>
            actorToCells.TryGetValue(a, out var list) ? list : System.Array.Empty<Hex>();

        public IGridActor Get(Hex c) => cellToActor.TryGetValue(c, out var a) ? a : null;

        bool IsBlockedInternal(Hex c, IGridActor ignore, bool includeTemp)
        {
            if (cellToActor.TryGetValue(c, out var a) && a != null && a != ignore)
                return true;
            if (includeTemp && tempCellToActor.TryGetValue(c, out var temp) && temp != null && temp != ignore)
                return true;
            return false;
        }

        public bool IsBlocked(Hex c, IGridActor ignore = null) => IsBlockedInternal(c, ignore, true);

        bool CanPlaceInternal(IGridActor a, Hex anchor, Facing4 facing, IGridActor ignore, bool includeTemp)
        {
            if (a?.Footprint == null)
                return Layout.Contains(anchor) && !IsBlockedInternal(anchor, ignore, includeTemp);

            foreach (var c in HexFootprint.Expand(anchor, facing, a.Footprint))
            {
                if (!Layout.Contains(c)) return false;
                if (IsBlockedInternal(c, ignore, includeTemp)) return false;
            }
            return true;
        }

        public bool CanPlace(IGridActor a, Hex anchor, Facing4 facing, IGridActor ignore = null)
            => CanPlaceInternal(a, anchor, facing, ignore, true);

        public bool CanPlaceIgnoringTemp(IGridActor a, Hex anchor, Facing4 facing, IGridActor ignore = null)
            => CanPlaceInternal(a, anchor, facing, ignore, false);

        public bool CanPlaceIgnoreTempAttack(IGridActor a, Hex anchor, Facing4 facing, IGridActor ignore = null)
            => CanPlaceInternal(a, anchor, facing, ignore, false);

        public bool TryPlace(IGridActor a, Hex anchor, Facing4 facing)
        {
            if (!CanPlace(a, anchor, facing)) return false;
            Remove(a);

            var cells = new List<Hex>();
            foreach (var c in HexFootprint.Expand(anchor, facing, a.Footprint))
            {
                cellToActor[c] = a;
                cells.Add(c);
            }
            actorToCells[a] = cells;

            a.Anchor = anchor;
            a.Facing = facing;
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

        public bool TempReserve(Hex cell, IGridActor owner)
        {
            if (owner == null) return false;
            if (!Layout.Contains(cell)) return false;

            if (cellToActor.TryGetValue(cell, out var blocker) && blocker != null && blocker != owner)
                return false;

            if (tempCellToActor.TryGetValue(cell, out var existing) && existing == owner)
                return false;

            if (tempCellToActor.TryGetValue(cell, out var prev) && prev != null && prev != owner)
            {
                if (tempActorToCells.TryGetValue(prev, out var prevCells))
                    prevCells.Remove(cell);
            }

            tempCellToActor[cell] = owner;

            if (!tempActorToCells.TryGetValue(owner, out var set))
            {
                set = new HashSet<Hex>();
                tempActorToCells[owner] = set;
            }
            set.Add(cell);
            return true;
        }

        public int TempClearForOwner(IGridActor owner)
        {
            if (owner == null) return 0;
            if (!tempActorToCells.TryGetValue(owner, out var cells) || cells == null)
                return 0;

            int count = cells.Count;
            foreach (var cell in cells)
            {
                if (tempCellToActor.TryGetValue(cell, out var who) && who == owner)
                    tempCellToActor.Remove(cell);
            }

            tempActorToCells.Remove(owner);
            return count;
        }

        public bool IsReservedTempAttack(Hex cell, IGridActor ignore = null)
        {
            if (!tempCellToActor.TryGetValue(cell, out var owner) || owner == null)
                return false;
            if (ignore != null && owner == ignore)
                return false;
            return true;
        }

        public int ClearLayer(OccLayer layer)
        {
            switch (layer)
            {
                case OccLayer.TempAttack:
                    int count = tempCellToActor.Count;
                    tempCellToActor.Clear();
                    tempActorToCells.Clear();
                    return count;
                default:
                    return 0;
            }
        }
    }
}