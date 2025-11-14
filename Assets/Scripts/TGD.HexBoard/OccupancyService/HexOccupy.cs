// File: TGD.HexBoard/HexOccupancy.cs
using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;


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
        readonly Dictionary<Hex, IGridActor> softCellToActor = new Dictionary<Hex, IGridActor>();
        readonly Dictionary<IGridActor, HashSet<Hex>> softActorToCells = new Dictionary<IGridActor, HashSet<Hex>>();

        public HexOccupancy(HexBoardLayout layout) { Layout = layout; }

        public IReadOnlyList<Hex> CellsOf(IGridActor a) =>
            actorToCells.TryGetValue(a, out var list) ? list : System.Array.Empty<Hex>();

        public IGridActor Get(Hex c) => cellToActor.TryGetValue(c, out var a) ? a : null;

        public bool TryGetActor(Hex cell, out IGridActor actor)
        {
            if (cellToActor.TryGetValue(cell, out var found) && found != null)
            {
                actor = found;
                return true;
            }

            actor = null;
            return false;
        }

        public bool TryGetAnchor(Unit unit, out Hex anchor)
        {
            if (unit != null)
            {
                anchor = unit.Position;
                return true;
            }

            anchor = Hex.Zero;
            return false;
        }

        bool IsBlockedInternal(Hex c, IGridActor ignore, bool includeTemp)
        {
            if (cellToActor.TryGetValue(c, out var a) && a != null && a != ignore)
                return true;
            if (includeTemp && tempCellToActor.TryGetValue(c, out var temp) && temp != null && temp != ignore)
                return true;
            return false;
        }

        public bool IsBlocked(Hex c, IGridActor ignore = null) => IsBlockedInternal(c, ignore, true);

        public bool IsBlockedFormal(Hex c, IGridActor ignore = null)
        {
            if (Layout != null && !Layout.Contains(c))
                return true;
            return IsBlockedInternal(c, ignore, false);
        }

        bool CanPlaceInternal(IGridActor a, Hex anchor, Facing4 facing, IGridActor ignore, bool includeTemp)
        {
            if (!Layout.Contains(anchor))
                return false;

            return !IsBlockedInternal(anchor, ignore, includeTemp);
        }

        public bool CanPlace(IGridActor a, Hex anchor, Facing4 facing, IGridActor ignore = null)
            => CanPlaceInternal(a, anchor, facing, ignore, true);

        public bool CanPlaceIgnoringTemp(IGridActor a, Hex anchor, Facing4 facing, IGridActor ignore = null)
            => CanPlaceInternal(a, anchor, facing, ignore, false);

        public bool TryPlace(IGridActor a, Hex anchor, Facing4 facing)
        {
            if (!CanPlace(a, anchor, facing, a)) return false;
            Remove(a);

            var cells = new List<Hex> { anchor };
            cellToActor[anchor] = a;
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

        public bool TempReserveSoft(Hex cell, IGridActor owner)
        {
            if (owner == null) return false;
            if (!Layout.Contains(cell)) return false;

            if (softCellToActor.TryGetValue(cell, out var existing) && existing == owner)
                return false;

            if (softCellToActor.TryGetValue(cell, out var prev) && prev != null && prev != owner)
            {
                if (softActorToCells.TryGetValue(prev, out var prevCells))
                {
                    prevCells.Remove(cell);
                    if (prevCells.Count == 0)
                        softActorToCells.Remove(prev);
                }
            }

            softCellToActor[cell] = owner;

            if (!softActorToCells.TryGetValue(owner, out var set) || set == null)
            {
                set = new HashSet<Hex>();
                softActorToCells[owner] = set;
            }

            set.Add(cell);
            return true;
        }

        public int TempClearSoftForOwner(IGridActor owner)
        {
            if (owner == null) return 0;
            if (!softActorToCells.TryGetValue(owner, out var cells) || cells == null)
                return 0;

            int count = cells.Count;
            foreach (var cell in cells)
            {
                if (softCellToActor.TryGetValue(cell, out var who) && who == owner)
                    softCellToActor.Remove(cell);
            }

            softActorToCells.Remove(owner);
            return count;
        }

        public bool TempRelease(Hex cell, IGridActor owner)
        {
            if (owner == null)
                return false;

            if (!tempCellToActor.TryGetValue(cell, out var existing) || existing != owner)
                return false;

            tempCellToActor.Remove(cell);
            if (tempActorToCells.TryGetValue(owner, out var set) && set != null)
            {
                set.Remove(cell);
                if (set.Count == 0)
                    tempActorToCells.Remove(owner);
            }

            return true;
        }

        public int TempRelease(IGridActor owner, IEnumerable<Hex> cells)
        {
            if (owner == null || cells == null)
                return 0;

            int count = 0;
            foreach (var cell in cells)
            {
                if (TempRelease(cell, owner))
                    count++;
            }

            return count;
        }

        public bool TempReleaseSoft(Hex cell, IGridActor owner)
        {
            if (owner == null)
                return false;

            if (!softCellToActor.TryGetValue(cell, out var existing) || existing != owner)
                return false;

            softCellToActor.Remove(cell);

            if (softActorToCells.TryGetValue(owner, out var set) && set != null)
            {
                set.Remove(cell);
                if (set.Count == 0)
                    softActorToCells.Remove(owner);
            }

            return true;
        }

        public int TempReleaseSoft(IGridActor owner, IEnumerable<Hex> cells)
        {
            if (owner == null || cells == null)
                return 0;

            int count = 0;
            foreach (var cell in cells)
            {
                if (TempReleaseSoft(cell, owner))
                    count++;
            }

            return count;
        }

        public int ClearSoftLayer()
        {
            int count = softCellToActor.Count;
            softCellToActor.Clear();
            softActorToCells.Clear();
            return count;
        }

        public bool IsSoftReserved(Hex cell)
        {
            return softCellToActor.ContainsKey(cell);
        }

        public int SoftReservedCount => softCellToActor.Count;

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

        public bool TryGetTempOwner(Hex cell, out IGridActor owner)
        {
            if (tempCellToActor.TryGetValue(cell, out var found) && found != null)
            {
                owner = found;
                return true;
            }

            owner = null;
            return false;
        }

        public bool TryGetSoftOwner(Hex cell, out IGridActor owner)
        {
            if (softCellToActor.TryGetValue(cell, out var found) && found != null)
            {
                owner = found;
                return true;
            }

            owner = null;
            return false;
        }

        public IEnumerable<IGridActor> EnumerateActors()
        {
            foreach (var pair in actorToCells)
            {
                if (pair.Key != null)
                    yield return pair.Key;
            }
        }

        public bool TryFindCoveringActor(Hex cell, out IGridActor actor, out bool isAnchorCell)
        {
            if (TryGetActor(cell, out var anchorActor) && anchorActor != null)
            {
                actor = anchorActor;
                isAnchorCell = true;
                return true;
            }

            IGridActor best = null;
            int bestDistance = int.MaxValue;

            foreach (var candidate in EnumerateActors())
            {
                if (candidate == null)
                    continue;

                if (candidate.Anchor.Equals(cell))
                {
                    actor = candidate;
                    isAnchorCell = true;
                    return true;
                }

                var shape = candidate.HitShape;
                if (shape == null)
                    continue;

                foreach (var covered in HexHitShape.Expand(candidate.Anchor, candidate.Facing, shape))
                {
                    if (!covered.Equals(cell))
                        continue;

                    int dist = Hex.Distance(candidate.Anchor, cell);
                    if (dist < bestDistance)
                    {
                        best = candidate;
                        bestDistance = dist;
                    }
                    else if (dist == bestDistance && best != null && Random.value < 0.5f)
                    {
                        best = candidate;
                    }
                }
            }

            if (best != null)
            {
                actor = best;
                isAnchorCell = false;
                return true;
            }

            actor = null;
            isAnchorCell = false;
            return false;
        }
    }
}
