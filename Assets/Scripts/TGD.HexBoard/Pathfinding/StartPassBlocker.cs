using TGD.HexBoard;

namespace TGD.HexBoard.Pathfinding
{
    public interface IPathBlocker
    {
        bool IsBlocked(Hex h);
    }

    public sealed class StartPassBlocker : IPathBlocker
    {
        readonly HexOccupancy _occ;
        readonly Hex _start;

        public StartPassBlocker(HexOccupancy occ, Unit self, Hex start)
        {
            _occ = occ;
            _start = start;
        }

        public bool IsBlocked(Hex h)
        {
            if (h.Equals(_start))
                return false;
            if (_occ == null)
                return true;
            return _occ.IsBlockedFormal(h);
        }
    }
}
