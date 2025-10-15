using System;

namespace TGD.CombatV2.Targeting
{
    public enum TargetMode
    {
        AnyClick,
        GroundOnly,
        EnemyOnly,
        AllyOnly,
        SelfOnly,
        EnemyOrGround,
        AllyOrGround,
        AnyUnit
    }

    public static class TargetingPresets
    {
        public static TargetingSpec For(TargetMode mode, int maxRange = -1)
        {
            switch (mode)
            {
                case TargetMode.AnyClick:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Any,
                        terrain = TargetTerrainMask.Any,
                        allowSelf = true,
                        requireEmpty = false,
                        requireOccupied = false,
                        maxRangeHexes = maxRange
                    };
                case TargetMode.GroundOnly:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Empty,
                        terrain = TargetTerrainMask.NonObstacle,
                        allowSelf = false,
                        requireEmpty = true,
                        requireOccupied = false,
                        maxRangeHexes = maxRange
                    };
                case TargetMode.EnemyOnly:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Enemy,
                        terrain = TargetTerrainMask.Any,
                        allowSelf = false,
                        requireEmpty = false,
                        requireOccupied = true,
                        maxRangeHexes = maxRange
                    };
                case TargetMode.AllyOnly:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Ally,
                        terrain = TargetTerrainMask.Any,
                        allowSelf = false,
                        requireEmpty = false,
                        requireOccupied = true,
                        maxRangeHexes = maxRange
                    };
                case TargetMode.SelfOnly:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Self,
                        terrain = TargetTerrainMask.Any,
                        allowSelf = true,
                        requireEmpty = false,
                        requireOccupied = true,
                        maxRangeHexes = maxRange
                    };
                case TargetMode.EnemyOrGround:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Enemy | TargetOccupantMask.Empty,
                        terrain = TargetTerrainMask.Any,
                        allowSelf = false,
                        requireEmpty = false,
                        requireOccupied = false,
                        maxRangeHexes = maxRange
                    };
                case TargetMode.AllyOrGround:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Ally | TargetOccupantMask.Empty,
                        terrain = TargetTerrainMask.Any,
                        allowSelf = false,
                        requireEmpty = false,
                        requireOccupied = false,
                        maxRangeHexes = maxRange
                    };
                case TargetMode.AnyUnit:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Ally | TargetOccupantMask.Enemy | TargetOccupantMask.Self,
                        terrain = TargetTerrainMask.Any,
                        allowSelf = true,
                        requireEmpty = false,
                        requireOccupied = true,
                        maxRangeHexes = maxRange
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }
    }
}

