// File: TGD.HexBoard/Environment/HexEnvironmentSystem.cs
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    public sealed class HexEnvironmentSystem : MonoBehaviour, IStickyMoveSource
    {
        [Header("Refs")]
        public HexBoardAuthoringLite authoring;

        [Serializable]
        public struct HazardZone
        {
            public HazardType type;
            public int q;
            public int r;
            [Min(0)] public int radius;
            public bool centerOnly;

            public Hex Center => new Hex(q, r);
        }

        [Header("Hazards (optional)")]
        public List<HazardZone> hazards = new();

        public bool HasHazard(Hex h, HazardKind kind)
        {
            if (hazards == null) return false;
            for (int i = 0; i < hazards.Count; i++)
            {
                var z = hazards[i];
                if (!z.type || z.type.kind != kind) continue;

                bool inRange = z.centerOnly ? h.Equals(z.Center) : Hex.Distance(h, z.Center) <= z.radius;
                if (inRange) return true;
            }
            return false;
        }

        public bool IsTrap(Hex h) => HasHazard(h, HazardKind.Trap);
        public bool IsPit(Hex h) => HasHazard(h, HazardKind.Pit);

        [Header("Default")]
        [Tooltip("全局默认地形速度倍率。1 = 正常速度，0.5 = 减速 50%，1.5 = 加速 50%。")]
        [Range(0.1f, 5f)] public float defaultSpeedMult = 1f;

        [Serializable]
        public struct SpeedPatch
        {
            public int q;
            public int r;
            [Min(0)] public int radius;
            [Range(0.1f, 5f)] public float mult;
            [Tooltip("进入该地块时刷新粘滞持续回合。>0 时生效。")]
            public int stickyTurnsOnEnter;

            public Hex Center => new Hex(q, r);
        }

        [Header("Speed Patches (optional)")]
        public List<SpeedPatch> patches = new();

        void Reset()
        {
            if (!authoring) authoring = FindFirstObjectByType<HexBoardAuthoringLite>();
        }

        public static HexEnvironmentSystem FindInScene()
            => FindFirstObjectByType<HexEnvironmentSystem>();

        public float GetSpeedMult(Hex h)
        {
            float m = Mathf.Clamp(defaultSpeedMult, 0.1f, 5f);
            if (patches != null)
            {
                for (int i = 0; i < patches.Count; i++)
                {
                    var p = patches[i];
                    if (Hex.Distance(h, p.Center) <= p.radius)
                        m *= Mathf.Clamp(p.mult, 0.1f, 5f);
                }
            }
            return Mathf.Clamp(m, 0.1f, 5f);
        }

        public bool IsStructBlocked(Hex h) => false;
        public bool IsLethalOnEnter(Hex h) => false;

        public bool TryGetSticky(Hex at, out float multiplier, out int durationTurns, out string tag)
        {
            multiplier = 1f;
            durationTurns = 0;
            tag = null;

            if (patches != null)
            {
                for (int i = 0; i < patches.Count; i++)
                {
                    var p = patches[i];
                    if (Hex.Distance(at, p.Center) > p.radius) continue;

                    float m = Mathf.Clamp(p.mult, 0.01f, 100f);
                    bool isHaste = m > 1f + 1e-4f;
                    bool isSlow = m < 1f - 1e-4f;

                    if (isHaste)
                    {
                        int turns = Mathf.Max(1, p.stickyTurnsOnEnter);
                        multiplier = m;
                        durationTurns = turns;
                        tag = $"Patch@{p.q},{p.r}";
                        return true;
                    }

                    if (isSlow && p.stickyTurnsOnEnter > 0)
                    {
                        multiplier = m;
                        durationTurns = p.stickyTurnsOnEnter;
                        tag = $"Patch@{p.q},{p.r}";
                        return true;
                    }
                }
            }

            return false;
        }

        public void ApplyHazardCircle(Hex center, int radius, HazardType type, int durationSeconds)
        {
            // TODO: runtime hazard spawning not implemented yet.
        }
    }
}
