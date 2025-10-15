using System.Collections.Generic;
using TGD.HexBoard;

namespace TGD.HexBoard.Path
{
    // 正式占位为阻挡（单位也算阻挡）
    public sealed class FormalUnitsBlockPassability : IPassability
    {
        readonly HexOccupancy _occ;
        readonly IGridActor _self;

        public FormalUnitsBlockPassability(HexOccupancy occ, IGridActor self)
        {
            _occ = occ;
            _self = self;
        }

        public bool IsBlocked(Hex h)
        {
            if (_occ == null)
                return true;

            if (_self != null)
            {
                if (!_occ.CanPlaceIgnoringTemp(_self, h, _self.Facing, ignore: _self))
                    return true;
                return false;
            }

            return _occ.IsBlockedFormal(h);
        }
    }

    // 只把“静态地形”当阻挡（被任何 Actor 占用的格不算地形阻挡）
    public sealed class StaticTerrainOnlyPassability : IPassability
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

            if (!_occ.IsBlockedFormal(h))
                return false;

            if (_occ.TryGetActor(h, out var actor) && actor != null)
                return false;

            return true;
        }
    }

    // 起点整块脚印放行（不是只放锚点单格）
    public sealed class StartFootprintDecorator : IPassability
    {
        readonly IPassability _inner;
        readonly HashSet<Hex> _selfCells = new();

        public StartFootprintDecorator(IPassability inner, PassabilityContext ctx)
        {
            _inner = inner;

            var occ = ctx.Occupancy;
            var self = ctx.Self;
            if (occ == null || self == null)
                return;

            var cells = occ.CellsOf(self);
            if (cells != null && cells.Count > 0)
            {
                foreach (var cell in cells)
                    _selfCells.Add(cell);
                return;
            }

            var anchor = self.Anchor;
            var footprint = self.Footprint;
            if (footprint != null)
            {
                foreach (var cell in HexFootprint.Expand(anchor, self.Facing, footprint))
                    _selfCells.Add(cell);
            }
            else
            {
                _selfCells.Add(anchor);
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
        // 精准移动/靠近移动：单位阻挡 + 整块脚印放行
        public static IPassability ForMove(HexOccupancy occ, IGridActor self)
        {
            var basePass = new FormalUnitsBlockPassability(occ, self);
            return new StartFootprintDecorator(basePass, new PassabilityContext(occ, self));
        }

        public static IPassability ForApproach(HexOccupancy occ, IGridActor self)
            => ForMove(occ, self);

        // 给 TargetValidator 的地形过滤使用
        public static IPassability StaticTerrainOnly(HexOccupancy occ)
            => new StaticTerrainOnlyPassability(occ);
    }
}
