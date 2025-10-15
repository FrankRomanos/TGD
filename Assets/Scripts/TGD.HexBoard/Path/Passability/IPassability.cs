using TGD.HexBoard;

namespace TGD.HexBoard.Path
{
    public interface IPassability
    {
        bool IsBlocked(Hex h);
    }

    public readonly struct PassabilityContext
    {
        public readonly HexOccupancy Occupancy;
        public readonly IGridActor Self;

        public PassabilityContext(HexOccupancy occupancy, IGridActor self)
        {
            Occupancy = occupancy;
            Self = self;
        }
    }
}
