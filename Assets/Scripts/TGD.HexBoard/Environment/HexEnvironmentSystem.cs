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
            public int q, r;
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
                bool inRange = z.centerOnly ? h.Equals(z.Center) : (Hex.Distance(h, z.Center) <= z.radius);
                if (inRange) return true;
            }
            return false;
        }

        public bool IsTrap(Hex h) => HasHazard(h, HazardKind.Trap);
        public bool IsPit(Hex h) => HasHazard(h, HazardKind.Pit);


        [Header("Default")]
        [Tooltip("全图默认速度倍率（1=正常，0.5=减速50%，1.5=加速50%）")]
        [Range(0.1f, 5f)] public float defaultSpeedMult = 1f;

        [Serializable]
        public struct SpeedPatch
        {
            public int q;
            public int r;
            [Min(0)] public int radius;
            [Range(0.1f, 5f)] public float mult;

            [Tooltip("进入该区时附着的持续回合数（>0时生效，仅用于 mult>1 的加速）")]
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

        /// 读某格的“区域速度倍率”（只影响站在区域内的实时速度；不含黏性）
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

        // ========= 黏性移速接口实现 =========
        // 规则：
        // - Trap 命中：若 stickyDurationTurns>0，则返回其 stickyMoveMult / stickyDurationTurns（常用于“毒池”→黏性减速）
        // - SpeedPatch 命中：当 mult>1 且 stickyTurnsOnEnter>0 时，返回 mult / stickyTurnsOnEnter（黏性加速）
        // - 区域减速（mult<1）不附着（离开即恢复）
        // 在 HexEnvironmentSystem 类声明处加：public sealed class HexEnvironmentSystem : MonoBehaviour, IStickyMoveSource

        // 类内追加：
        public bool TryGetSticky(Hex at, out float multiplier, out int durationTurns, out string tag)
        {
            multiplier = 1f; durationTurns = 0; tag = null;

            // 只对“加速 Patch”赋予 1 回合贴附；减速 Patch 不贴附
            if (patches != null)
            {
                for (int i = 0; i < patches.Count; i++)
                {
                    var p = patches[i];
                    if (Hex.Distance(at, p.Center) <= p.radius)
                    {
                        float m = Mathf.Clamp(p.mult, 0.01f, 100f);
                        if (m > 1.001f)
                        {
                            multiplier = m;
                            durationTurns = 1; // 规则：加速至少一回合
                            tag = $"Patch@{p.q},{p.r}";
                            return true;
                        }
                    }
                }
            }

            // （如果你以后希望某些 Hazard 也贴附，这里再判定 HazardZone，并设 tag="Hazard@{type.hazardId}@{center.q},{center.r}"）

            return false;
        }


        // 预留：把“燃烧/酸液”等按圈应用
        public void ApplyHazardCircle(Hex center, int radius, HazardType type, int durationSeconds)
        {
            // TODO
        }
    }
}
