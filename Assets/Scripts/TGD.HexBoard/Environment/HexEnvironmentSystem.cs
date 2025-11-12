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

        public bool IsTrap(Hex h)
            => envMap != null && (envMap.HasKind(h, HazardKind.Trap) || envMap.HasKind(h, HazardKind.EntangleTrap));
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

            if (envMap == null)
                return false;

            var effects = envMap.Get(at);
            if (effects == null || effects.Count == 0)
                return false;

            HazardType selected = null;
            float selectedMult = 1f;
            int selectedTurns = 0;
            string selectedTag = null;

            for (int i = 0; i < effects.Count; i++)
            {
                var hazard = effects[i].Hazard;
                if (hazard == null)
                    continue;

                int configuredTurns = hazard.stickyDurationTurns;
                if (configuredTurns == 0)
                    continue;

                float configuredMult = Mathf.Clamp(hazard.stickyMoveMult, 0.1f, 5f);
                if (Mathf.Approximately(configuredMult, 1f))
                    continue;

                int normalizedTurns;
                if (configuredTurns < 0)
                {
                    normalizedTurns = -1;
                }
                else
                {
                    normalizedTurns = Mathf.Max(1, configuredTurns);
                }
                string candidateTag = BuildStickyTag(hazard, at);

                if (selected == null || ShouldPreferSticky(configuredMult, normalizedTurns, hazard.kind, selectedMult, selectedTurns, selected.kind))
                {
                    selected = hazard;
                    selectedMult = configuredMult;
                    selectedTurns = normalizedTurns;
                    selectedTag = candidateTag;
                }
            }

            if (selected == null)
                return false;

            multiplier = selectedMult;
            durationTurns = selectedTurns;
            tag = selectedTag;
            return true;
        }

        static string BuildStickyTag(HazardType hazard, Hex at)
        {
            if (hazard == null)
                return $"Patch@{at.q},{at.r}";

            string prefix = !string.IsNullOrEmpty(hazard.hazardId)
                ? hazard.hazardId
                : (!string.IsNullOrEmpty(hazard.name) ? hazard.name : hazard.kind.ToString());

            return $"Hazard@{prefix}@{at.q},{at.r}";
        }

        static bool ShouldPreferSticky(float candidateMult, int candidateTurns, HazardKind candidateKind,
            float currentMult, int currentTurns, HazardKind currentKind)
        {
            float candidateDelta = Mathf.Abs(candidateMult - 1f);
            float currentDelta = Mathf.Abs(currentMult - 1f);

            if (candidateDelta > currentDelta + 1e-4f)
                return true;
            if (candidateDelta + 1e-4f < currentDelta)
                return false;

            if (candidateTurns < 0 && currentTurns >= 0)
                return true;
            if (candidateTurns >= 0 && currentTurns < 0)
                return false;

            if (candidateKind != currentKind)
            {
                if (candidateKind == HazardKind.SpeedPatch)
                    return true;
                if (currentKind == HazardKind.SpeedPatch)
                    return false;
            }

            return false;
        }

        public void ApplyHazardCircle(Hex center, int radius, HazardType type, int durationSeconds)
        {
            // TODO: runtime hazard spawning not implemented yet.
        }
    }
}
