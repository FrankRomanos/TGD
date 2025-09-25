using System;
using System.Collections.Generic;
using TGD.Core;
using TGD.Data;
using UnityEngine;

namespace TGD.Combat
{
    /// <summary>
    /// Describes the default class resource profiles for each class identifier.
    /// </summary>
    public static class ClassResourceCatalog
    {
        public readonly struct ClassResourceProfile
        {
            public ClassResourceProfile(CostResourceType resourceType, int defaultMax, int defaultStart)
            {
                if (resourceType == CostResourceType.Custom)
                    throw new ArgumentException("Custom resource cannot be part of class defaults.", nameof(resourceType));

                ResourceType = resourceType;
                DefaultMax = Mathf.Max(0, defaultMax);
                DefaultStart = Mathf.Clamp(defaultStart, 0, DefaultMax);
            }

            public CostResourceType ResourceType { get; }
            public int DefaultMax { get; }
            public int DefaultStart { get; }
        }

        private static readonly Dictionary<string, ClassResourceProfile[]> ProfilesByClass =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "CL001", new[] { new ClassResourceProfile(CostResourceType.Discipline, 5, 0) } },
                { "CL002", new[] { new ClassResourceProfile(CostResourceType.Discipline, 5, 0) } },
                { "CL003", new[] { new ClassResourceProfile(CostResourceType.Discipline, 5, 0) } },

                { "CL011", new[] { new ClassResourceProfile(CostResourceType.Iron, 5, 0) } },
                { "CL012", new[] { new ClassResourceProfile(CostResourceType.Iron, 5, 0) } },
                { "CL013", new[]
                    {
                        new ClassResourceProfile(CostResourceType.Iron, 5, 0),
                        new ClassResourceProfile(CostResourceType.posture, 4, 0)
                    }
                },

                { "CL021", new[] { new ClassResourceProfile(CostResourceType.Rage, 100, 0) } },
                { "CL022", new[] { new ClassResourceProfile(CostResourceType.Versatility, 5, 0) } },

                { "CL031", new[] { new ClassResourceProfile(CostResourceType.Gunpowder, 5, 0) } },
                { "CL032", new[] { new ClassResourceProfile(CostResourceType.point, 3, 0) } },
                { "CL041", new[] { new ClassResourceProfile(CostResourceType.combo, 5, 0) } },
                { "CL042", new[] { new ClassResourceProfile(CostResourceType.point, 3, 0) } },

                { "CL051", new[] { new ClassResourceProfile(CostResourceType.punch, 5, 0) } },
                { "CL053", new[] { new ClassResourceProfile(CostResourceType.qi, 4, 0) } },
                { "CL071", new[] { new ClassResourceProfile(CostResourceType.vision, 5, 0) } },
            };

        private static readonly ClassResourceProfile[] EmptyProfiles = Array.Empty<ClassResourceProfile>();

        public static IReadOnlyList<ClassResourceProfile> GetProfiles(string classId)
        {
            if (string.IsNullOrWhiteSpace(classId))
                return EmptyProfiles;

            return ProfilesByClass.TryGetValue(classId, out var list) ? list : EmptyProfiles;
        }

        public static IReadOnlyList<CostResourceType> GetResourceTypes(string classId)
        {
            if (string.IsNullOrWhiteSpace(classId))
                return Array.Empty<CostResourceType>();

            if (!ProfilesByClass.TryGetValue(classId, out var profiles) || profiles == null || profiles.Length == 0)
                return Array.Empty<CostResourceType>();

            var list = new List<CostResourceType>(profiles.Length);
            foreach (var profile in profiles)
                list.Add(profile.ResourceType);
            return list;
        }

        public static void ApplyDefaults(Stats stats, string classId, bool overwriteExisting = false)
        {
            if (stats == null)
                return;

            foreach (var profile in GetProfiles(classId))
                ResourceUtility.ApplyDefaults(stats, profile, overwriteExisting);
        }

        public static void ApplyDefaults(Unit unit, bool overwriteExisting = false)
        {
            if (unit == null)
                return;

            ApplyDefaults(unit.Stats, unit.ClassId, overwriteExisting);
        }
    }
}
