using TGD.HexBoard;

namespace TGD.CombatV2.Integration
{
    public interface IActorOccupancyBridge
    {
        bool IsReady { get; }
        object Actor { get; }
        Hex CurrentAnchor { get; }
        void EnsurePlacedNow();
        void MoveCommit(Hex newAnchor, Facing4 newFacing);
    }
}
