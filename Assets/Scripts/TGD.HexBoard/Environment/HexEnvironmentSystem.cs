using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    /// <summary>
    /// 环境系统（最小可用版）：
    /// - GetSpeedMult(Hex) → 加速/减速带影响“可达范围（加权寻路）”
    /// - 预留 Hazards/边界接口，以后逐步接入
    /// </summary>
    public sealed class HexEnvironmentSystem : MonoBehaviour
    {
        [Header("Refs")]
        public HexBoardAuthoringLite authoring; // 为了读 layout
        [System.Serializable]
        public struct HazardZone
        {
            public HazardType type;
            public int q, r;
            [Min(0)] public int radius;     // hex 距离；0=只中心
            public bool centerOnly;         // 勾选则只中心
            public Hex Center => new Hex(q, r);
        }

        [Header("Hazards (optional)")]
        public List<HazardZone> hazards = new();
        // 查询
        public bool HasHazard(Hex h, HazardKind kind)
        {
            if (hazards == null) return false;
            for (int i = 0; i < hazards.Count; i++)
            {
                var z = hazards[i];
                if (!z.type) continue;
                if (z.type.kind != kind) continue;
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
            [Min(0)] public int radius; // 以 Hex 距离
            [Range(0.1f, 5f)] public float mult;
            public Hex Center => new Hex(q, r);
        }

        [Header("Speed Patches (optional)")]
        public List<SpeedPatch> patches = new();

        void Reset()
        {
            if (!authoring) authoring = FindFirstObjectByType<HexBoardAuthoringLite>();
        }

        public static HexEnvironmentSystem FindInScene()
        {
            return FindFirstObjectByType<HexEnvironmentSystem>();
        }

        /// <summary> 读取某格的速度倍率（可叠加补丁，采用乘法叠加）。</summary>
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

        /// <summary>（预留）是否结构阻挡。第一步返回 false，继续用你现有占位/物理/边界判定。</summary>
        public bool IsStructBlocked(Hex h) => false;

        /// <summary>（预留）是否致命格（坑/悬崖等）。第一步不启用。</summary>
        public bool IsLethalOnEnter(Hex h) => false;

        // ========= Hazards（预留；后续步骤接入） =========
        // 先放接口：以后把“燃烧/酸液”等放进来
        public void ApplyHazardCircle(Hex center, int radius, HazardType type, int durationSeconds)
        {
            // 第一步不实现；留空即可
        }
    }
}

