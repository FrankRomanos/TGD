using UnityEngine;

namespace TGD.CoreV2
{
    public enum TargetSelectionMode
    {
        Single = 0
    }

    public enum CastRangeType
    {
        Infinite = 0,
        Fixed = 1,
        ByStat = 2
    }

    public enum CastShape
    {
        SingleCell = 0,
        Circle = 1,
        Cone60 = 2,
        Cone120 = 3,
        Line = 4
    }

    [System.Serializable]
    public struct TargetSelectionProfile
    {
        public TargetSelectionMode selectionMode;
        public CastRangeType rangeType;
        public int rangeValue;
        public CastShape shape;

        public static TargetSelectionProfile Default => new TargetSelectionProfile
        {
            selectionMode = TargetSelectionMode.Single,
            rangeType = CastRangeType.Infinite,
            rangeValue = 0,
            shape = CastShape.SingleCell
        };

        public int ResolveRange(UnitRuntimeContext context, int fallback = -1)
        {
            switch (rangeType)
            {
                case CastRangeType.Fixed:
                    return rangeValue >= 0 ? rangeValue : Mathf.Max(-1, fallback);
                case CastRangeType.ByStat:
                    if (context != null)
                        return Mathf.Max(0, context.MoveStepsCap);
                    if (rangeValue >= 0)
                        return rangeValue;
                    return Mathf.Max(-1, fallback);
                default:
                    return -1;
            }
        }

        public TargetSelectionProfile WithDefaults()
        {
            var profile = this;
            if (profile.selectionMode != TargetSelectionMode.Single)
                profile.selectionMode = TargetSelectionMode.Single;
            if (profile.rangeType == CastRangeType.Fixed && profile.rangeValue < 0)
                profile.rangeValue = 0;
            return profile;
        }
    }
}
