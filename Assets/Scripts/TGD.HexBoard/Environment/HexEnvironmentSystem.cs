using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    /// <summary>
    /// ����ϵͳ����С���ð棩��
    /// - GetSpeedMult(Hex) �� ����/���ٴ�Ӱ�조�ɴﷶΧ����ȨѰ·����
    /// - Ԥ�� Hazards/�߽�ӿڣ��Ժ��𲽽���
    /// </summary>
    public sealed class HexEnvironmentSystem : MonoBehaviour
    {
        [Header("Refs")]
        public HexBoardAuthoringLite authoring; // Ϊ�˶� layout
        [System.Serializable]
        public struct HazardZone
        {
            public HazardType type;
            public int q, r;
            [Min(0)] public int radius;     // hex ���룻0=ֻ����
            public bool centerOnly;         // ��ѡ��ֻ����
            public Hex Center => new Hex(q, r);
        }

        [Header("Hazards (optional)")]
        public List<HazardZone> hazards = new();
        // ��ѯ
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
        [Tooltip("ȫͼĬ���ٶȱ��ʣ�1=������0.5=����50%��1.5=����50%��")]
        [Range(0.1f, 5f)] public float defaultSpeedMult = 1f;

        [Serializable]
        public struct SpeedPatch
        {
            public int q;
            public int r;
            [Min(0)] public int radius; // �� Hex ����
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

        /// <summary> ��ȡĳ����ٶȱ��ʣ��ɵ��Ӳ��������ó˷����ӣ���</summary>
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

        /// <summary>��Ԥ�����Ƿ�ṹ�赲����һ������ false��������������ռλ/����/�߽��ж���</summary>
        public bool IsStructBlocked(Hex h) => false;

        /// <summary>��Ԥ�����Ƿ������񣨿�/���µȣ�����һ�������á�</summary>
        public bool IsLethalOnEnter(Hex h) => false;

        // ========= Hazards��Ԥ��������������룩 =========
        // �ȷŽӿڣ��Ժ�ѡ�ȼ��/��Һ���ȷŽ���
        public void ApplyHazardCircle(Hex center, int radius, HazardType type, int durationSeconds)
        {
            // ��һ����ʵ�֣����ռ���
        }
    }
}

