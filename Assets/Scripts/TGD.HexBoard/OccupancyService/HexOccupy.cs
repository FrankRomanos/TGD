using System;
using System.Collections.Generic;

namespace TGD.HexBoard
{
<<<<<<< HEAD
    /// <summary>
    /// Tracks cell occupancy and temporary reservations for grid actors on a hex board.
    /// </summary>
=======
    public enum OccLayer
    {
        Normal = 0,
        TempAttack = 1
    }

>>>>>>> c7f259781277cdba4eb7c94c163904c550c2915b
    public sealed class HexOccupancy
    {
        public HexBoardLayout Layout { get; }
        readonly Dictionary<Hex, IGridActor> cellToActor = new();
        readonly Dictionary<IGridActor, List<Hex>> actorToCells = new();
<<<<<<< HEAD
        readonly Dictionary<Hex, Dictionary<ReserveLayer, IGridActor>> reserveOwners = new();
        readonly Dictionary<IGridActor, HashSet<(Hex cell, ReserveLayer layer)>> actorReserves = new();
=======
        readonly Dictionary<Hex, IGridActor> tempCellToActor = new();
        readonly Dictionary<IGridActor, HashSet<Hex>> tempActorToCells = new();
>>>>>>> c7f259781277cdba4eb7c94c163904c550c2915b

        public HexOccupancy(HexBoardLayout layout) { Layout = layout; }

        public IReadOnlyList<Hex> CellsOf(IGridActor a) =>
                actorToCells.TryGetValue(a, out var list) ? list : Array.Empty<Hex>();

        public IGridActor Get(Hex c) => cellToActor.TryGetValue(c, out var a) ? a : null;
<<<<<<< HEAD
        public bool IsBlocked(Hex c, IGridActor ignore = null, ReserveLayer ignoreLayers = ReserveLayer.None)
        {
            if (cellToActor.TryGetValue(c, out var a) && a != null && a != ignore)
                return true;

            if (reserveOwners.TryGetValue(c, out var layers))
            {
                foreach (var kv in layers)
                {
                    var layer = kv.Key;
                    if ((ignoreLayers & layer) != 0)
                        continue;

                    var owner = kv.Value;
                    if (owner != null && owner != ignore)
                        return true;
                }
            }

            return false;
        }

        public bool CanPlace(IGridActor a, Hex anchor, Facing4 facing, IGridActor ignore = null, ReserveLayer ignoreLayers = ReserveLayer.None)
        {
            if (a?.Footprint == null) return Layout.Contains(anchor) && !IsBlocked(anchor, ignore, ignoreLayers);
            foreach (var c in HexFootprint.Expand(anchor, facing, a.Footprint))
            {
                if (!Layout.Contains(c)) return false;
                if (IsBlocked(c, ignore, ignoreLayers)) return false;
=======

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
>>>>>>> c7f259781277cdba4eb7c94c163904c550c2915b
            }
            return true;
        }

        public bool CanPlace(IGridActor a, Hex anchor, Facing4 facing, IGridActor ignore = null)
            => CanPlaceInternal(a, anchor, facing, ignore, true);

        public bool CanPlaceIgnoringTemp(IGridActor a, Hex anchor, Facing4 facing, IGridActor ignore = null)
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

        public void Remove(IGridActor a, bool clearReserves = false)
        {
            if (a == null) return;
            if (!actorToCells.TryGetValue(a, out var cells)) return;
            foreach (var c in cells)
                if (cellToActor.TryGetValue(c, out var who) && who == a)
                    cellToActor.Remove(c);
            actorToCells.Remove(a);
            if (clearReserves)
                ClearReserves(a, ReserveLayer.TempReserve | ReserveLayer.TempAttack);
        }

        public bool TryReserve(IGridActor actor, Hex cell, ReserveLayer layer)
        {
            if (actor == null) return false;
            if (layer == ReserveLayer.None) return false;
            if (!Layout.Contains(cell)) return false;

            if (!reserveOwners.TryGetValue(cell, out var layers))
            {
                layers = new Dictionary<ReserveLayer, IGridActor>();
                reserveOwners[cell] = layers;
            }

            if (layers.TryGetValue(layer, out var existing) && existing != null && existing != actor)
                return false;

            layers[layer] = actor;

            if (!actorReserves.TryGetValue(actor, out var set))
            {
                set = new HashSet<(Hex, ReserveLayer)>();
                actorReserves[actor] = set;
            }
            set.Add((cell, layer));
            return true;
        }

        public int ClearReserves(IGridActor actor, ReserveLayer layers)
        {
            if (actor == null) return 0;
            if (!actorReserves.TryGetValue(actor, out var set)) return 0;

            var toRemove = new List<(Hex cell, ReserveLayer layer)>();
            foreach (var entry in set)
            {
                if (layers == ReserveLayer.None || (entry.layer & layers) != 0)
                    toRemove.Add(entry);
            }

            foreach (var entry in toRemove)
            {
                if (reserveOwners.TryGetValue(entry.cell, out var map) &&
                    map.TryGetValue(entry.layer, out var owner) && owner == actor)
                {
                    map.Remove(entry.layer);
                    if (map.Count == 0)
                        reserveOwners.Remove(entry.cell);
                }
                set.Remove(entry);
            }

            if (set.Count == 0)
                actorReserves.Remove(actor);

            return toRemove.Count;
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
    [Flags]
    public enum ReserveLayer
    {
        None = 0,
        TempReserve = 1 << 0,
        TempAttack = 1 << 1,
    }
}
