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

    sealed class OccServicePassability : IPassability
    {
        readonly UnitRuntimeContext _ctx;
        readonly Facing4 _face;

        public OccServicePassability(UnitRuntimeContext ctx, IGridActor self)
        {
            _ctx = ctx;
            _face = self != null ? self.Facing : Facing4.PlusQ;
        }

        public bool IsBlocked(Hex h)
        {
            var service = _ctx?.occService;
            if (service == null)
                return true;
            return !service.IsFreeFor(_ctx, h, _face);
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
        static IPassability WithStart(IPassability inner, HexOccupancy occ, IGridActor self, Hex fallbackStart)
            => new StartFootprintDecorator(inner, occ, self, fallbackStart);

        public static IPassability ForMove(UnitRuntimeContext ctx, IGridActor self, Hex fallbackStart)
        {
            var occ = ctx?.occService?.Get();
            return WithStart(new OccServicePassability(ctx, self), occ, self, fallbackStart);
        }

        public static IPassability ForMove(HexOccupancy occ, IGridActor self, Hex fallbackStart)
            => WithStart(new FormalUnitsBlockPassability(occ), occ, self, fallbackStart);

        public static IPassability ForApproach(HexOccupancy occ, IGridActor self, Hex fallbackStart)
            => ForMove(occ, self, fallbackStart);

        public static IPassability ForApproach(UnitRuntimeContext ctx, IGridActor self, Hex fallbackStart)
            => ForMove(ctx, self, fallbackStart);

        public static IPassability StaticTerrainOnly(HexOccupancy occ)
            => new StaticTerrainOnlyPassability(occ);
    }
}
