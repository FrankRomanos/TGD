using System;
using TGD.HexBoard;

namespace TGD.CombatV2.Integration
{
    public interface IActorOccupancyBridge
    {
        bool IsReady { get; }
        object Actor { get; }
        Hex CurrentAnchor { get; }
        int AnchorVersion { get; }
        event System.Action<Hex, int> AnchorChanged;
        bool EnsurePlacedNow();
        bool MoveCommit(Hex newAnchor, Facing4 newFacing);
    }
}
