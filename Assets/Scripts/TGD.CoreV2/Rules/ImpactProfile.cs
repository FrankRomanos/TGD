using System;

namespace TGD.CoreV2
{
    public enum ImpactAnchor
    {
        SelfCell = 0,
        TargetCell = 1,
        TargetUnitCell = 2
    }

    public enum ImpactShape
    {
        Single = 0,
        Circle = 1,
        Sector60 = 2,
        Sector120 = 3,
        Line = 4
    }

    public enum ImpactTeamFilter
    {
        SelfOnly = 0,
        Allies = 1,
        AlliesAndSelf = 2,
        Enemies = 3,
        AllUnits = 4
    }

    public enum ImpactCountMode
    {
        All = 0,
        FirstN = 1,
        RandomN = 2
    }

    [Serializable]
    public struct ImpactProfile
    {
        public ImpactAnchor anchor;
        public ImpactShape shape;
        public int radius;
        public ImpactTeamFilter teamFilter;
        public ImpactCountMode countMode;
        public int maxTargets;

        public static ImpactProfile Default => new ImpactProfile
        {
            anchor = ImpactAnchor.SelfCell,
            shape = ImpactShape.Single,
            radius = 0,
            teamFilter = ImpactTeamFilter.AlliesAndSelf,
            countMode = ImpactCountMode.All,
            maxTargets = 0
        };

        public ImpactProfile WithDefaults()
        {
            var profile = this;
            if (profile.radius < 0)
                profile.radius = 0;
            if (profile.shape == ImpactShape.Single)
                profile.radius = 0;
            if (profile.maxTargets < 0)
                profile.maxTargets = 0;
            return profile;
        }
    }
}
