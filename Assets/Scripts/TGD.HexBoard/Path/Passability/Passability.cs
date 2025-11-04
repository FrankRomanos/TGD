using System.Collections.Generic;
using TGD.CoreV2;

namespace TGD.HexBoard.Path
{
    sealed class FormalUnitsBlockPassability : IPassability
    {
        readonly HexOccupancy _occ;

        public FormalUnitsBlockPassability(HexOccupancy occ)
        {
            _occ = occ;
        }

        public bool IsBlocked(Hex h)
        {
            if (_occ == null)
                return true;
            return _occ.IsBlockedFormal(h);
        }
    }

    sealed class StaticTerrainOnlyPassability : IPassability
    {
        readonly HexOccupancy _occ;

        public StaticTerrainOnlyPassability(HexOccupancy occ)
        {
            _occ = occ;
        }

        public bool IsBlocked(Hex h)
        {
            if (_occ == null)
                return true;
            return _occ.IsBlockedFormal(h) && !_occ.TryGetActor(h, out _);
        }
    }

    sealed class StartFootprintDecorator : IPassability
    {
        readonly IPassability _inner;
        readonly HashSet<Hex> _selfCells = new();

        public StartFootprintDecorator(IPassability inner, HexOccupancy occ, IGridActor self, Hex fallbackStart)
        {
            _inner = inner;
            if (self == null)
                return;

            if (occ != null)
            {
                var cells = occ.CellsOf(self);
                if (cells != null && cells.Count > 0)
                {
                    foreach (var cell in cells)
                        _selfCells.Add(cell);
                    return;
                }
            }

            var anchors = new HashSet<Hex>();
            anchors.Add(fallbackStart);
            anchors.Add(self.Anchor);

            foreach (var anchor in anchors)
            {
                if (self.Footprint != null)
                {
                    foreach (var cell in HexFootprint.Expand(anchor, self.Facing, self.Footprint))
                        _selfCells.Add(cell);
                }
                else
                {
                    _selfCells.Add(anchor);
                }
            }
        }

        public bool IsBlocked(Hex h)
        {
            if (_selfCells.Contains(h))
                return false;
            return _inner != null && _inner.IsBlocked(h);
        }
    }

    public static class PassabilityFactory
    {
        public static IPassability ForMove(HexOccupancy occ, IGridActor self, Hex fallbackStart)
            => new StartFootprintDecorator(new FormalUnitsBlockPassability(occ), occ, self, fallbackStart);

        public static IPassability ForApproach(HexOccupancy occ, IGridActor self, Hex fallbackStart)
            => ForMove(occ, self, fallbackStart);

        public static IPassability StaticTerrainOnly(HexOccupancy occ)
            => new StaticTerrainOnlyPassability(occ);
    }
}
