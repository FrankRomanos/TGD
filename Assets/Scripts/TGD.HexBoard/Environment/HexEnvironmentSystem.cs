// File: TGD.HexBoard/Environment/HexEnvironmentSystem.cs
using System;
using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    public sealed class HexEnvironmentSystem : MonoBehaviour, IStickyMoveSource
    {
        [Header("Refs")]
        public HexBoardAuthoringLite authoring;

        [Header("Environment Map")]
        [Tooltip("Scene singleton that stores environment cell effects (auto resolved).")]
        public EnvMapHost envMap;

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

        [Header("Legacy Hazards (ignored)")]
        [Tooltip("Legacy inspector data (ignored). Use Encounter envStamps instead.")]
        public List<HazardZone> hazards = new();

        public bool HasHazard(Hex h, HazardKind kind)
            => envMap != null && envMap.HasKind(h, kind);

        public bool IsTrap(Hex h) => envMap != null && envMap.HasKind(h, HazardKind.Trap);
        public bool IsPit(Hex h) => envMap != null && envMap.HasKind(h, HazardKind.Pit);

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

        [Header("Legacy Speed Patches (ignored)")]
        [Tooltip("Legacy inspector data (ignored). Use Encounter envStamps instead.")]
        public List<SpeedPatch> patches = new();

        void Reset()
        {
            if (!authoring) authoring = FindFirstObjectByType<HexBoardAuthoringLite>();
            if (!envMap) envMap = FindFirstObjectByType<EnvMapHost>();
        }

        public static HexEnvironmentSystem FindInScene()
            => FindFirstObjectByType<HexEnvironmentSystem>();

        public float GetSpeedMult(Hex h)
        {
            float m = Mathf.Clamp(defaultSpeedMult, 0.1f, 5f);

            if (envMap != null)
            {
                var effects = envMap.Get(h);
                foreach (var effect in effects)
                {
                    var hazard = effect.Hazard;
                    if (hazard == null)
                        continue;

                    float mult = Mathf.Clamp(hazard.stickyMoveMult, 0.1f, 5f);
                    if (!Mathf.Approximately(mult, 1f))
                        m *= mult;
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

            return false;
        }

        public void ApplyHazardCircle(Hex center, int radius, HazardType type, int durationSeconds)
        {
            // TODO: runtime hazard spawning not implemented yet.
        }
    }
}
