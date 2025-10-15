using System;

namespace TGD.CombatV2.Targeting
{
    [Flags]
    public enum TargetOccupantMask
    {
        None = 0,
        Empty = 1 << 0,
        Enemy = 1 << 1,
        Ally = 1 << 2,
        Self = 1 << 3,
        Any = Empty | Enemy | Ally | Self
    }

    [Flags]
    public enum TargetTerrainMask
    {
        NonObstacle = 1 << 0,
        Any = 1 << 1
    }

    public struct TargetingSpec
    {
        public TargetOccupantMask occupant;
        public TargetTerrainMask terrain;
        public bool allowSelf;
        public bool requireOccupied;
        public bool requireEmpty;

        public override string ToString()
        {
            return $"occ={occupant} terrain={terrain} allowSelf={allowSelf} requireOccupied={requireOccupied} requireEmpty={requireEmpty}";
        }
    }
}
