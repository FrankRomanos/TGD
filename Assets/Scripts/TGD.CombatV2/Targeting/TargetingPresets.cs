using TGD.CoreV2;

namespace TGD.CombatV2.Targeting
{
    public static class TargetingPresets
    {
        public static TargetingSpec For(TargetRule rule, int maxRange = -1)
        {
            switch (rule)
            {
                case TargetRule.GroundOnly:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Empty,
                        terrain = TargetTerrainMask.NonObstacle,
                        allowSelf = false,
                        requireEmpty = true,
                        requireOccupied = false,
                        maxRangeHexes = maxRange
                    };
                case TargetRule.EnemyOnly:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Enemy,
                        terrain = TargetTerrainMask.Any,
                        allowSelf = false,
                        requireEmpty = false,
                        requireOccupied = true,
                        maxRangeHexes = maxRange
                    };
                case TargetRule.AllyOnly:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Ally,
                        terrain = TargetTerrainMask.Any,
                        allowSelf = false,
                        requireEmpty = false,
                        requireOccupied = true,
                        maxRangeHexes = maxRange
                    };
                case TargetRule.SelfOnly:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Self,
                        terrain = TargetTerrainMask.Any,
                        allowSelf = true,
                        requireEmpty = false,
                        requireOccupied = false,
                        maxRangeHexes = maxRange
                    };
                case TargetRule.EnemyOrGround:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Enemy | TargetOccupantMask.Empty,
                        terrain = TargetTerrainMask.NonObstacle,
                        allowSelf = false,
                        requireEmpty = false,
                        requireOccupied = false,
                        maxRangeHexes = maxRange
                    };
                case TargetRule.AllyOrGround:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Ally | TargetOccupantMask.Empty,
                        terrain = TargetTerrainMask.Any,
                        allowSelf = false,
                        requireEmpty = false,
                        requireOccupied = false,
                        maxRangeHexes = maxRange
                    };
                case TargetRule.AnyUnit:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Ally | TargetOccupantMask.Enemy | TargetOccupantMask.Self,
                        terrain = TargetTerrainMask.Any,
                        allowSelf = true,
                        requireEmpty = false,
                        requireOccupied = true,
                        maxRangeHexes = maxRange
                    };
                case TargetRule.AnyClick:
                default:
                    return new TargetingSpec
                    {
                        occupant = TargetOccupantMask.Any,
                        terrain = TargetTerrainMask.Any,
                        allowSelf = true,
                        requireEmpty = false,
                        requireOccupied = false,
                        maxRangeHexes = maxRange
                    };
            }
        }
    }
}

