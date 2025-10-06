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
        [Tooltip("ȫͼĬ���ٶȱ��ʣ�1=������0.5=����50%��1.5=����50%��")]
        [Range(0.1f, 5f)] public float defaultSpeedMult = 1f;

        [Serializable]
        public struct SpeedPatch
        {
            public int q;
            public int r;
            [Min(0)] public int radius;
            [Range(0.1f, 5f)] public float mult;

            [Tooltip("�������ʱ���ŵĳ����غ�����>0ʱ��Ч�������� mult>1 �ļ��٣�")]
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

        /// ��ĳ��ġ������ٶȱ��ʡ���ֻӰ��վ�������ڵ�ʵʱ�ٶȣ�������ԣ�
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

        // ========= ������ٽӿ�ʵ�� =========
        // ����
        // - Trap ���У��� stickyDurationTurns>0���򷵻��� stickyMoveMult / stickyDurationTurns�������ڡ����ء�����Լ��٣�
        // - SpeedPatch ���У��� mult>1 �� stickyTurnsOnEnter>0 ʱ������ mult / stickyTurnsOnEnter����Լ��٣�
        // - ������٣�mult<1�������ţ��뿪���ָ���
        // �� HexEnvironmentSystem ���������ӣ�public sealed class HexEnvironmentSystem : MonoBehaviour, IStickyMoveSource

        // ����׷�ӣ�
        public bool TryGetSticky(Hex at, out float multiplier, out int durationTurns, out string tag)
        {
            multiplier = 1f; durationTurns = 0; tag = null;

            // ֻ�ԡ����� Patch������ 1 �غ����������� Patch ������
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
                            durationTurns = 1; // ���򣺼�������һ�غ�
                            tag = $"Patch@{p.q},{p.r}";
                            return true;
                        }
                    }
                }
            }

            // ��������Ժ�ϣ��ĳЩ Hazard Ҳ�������������ж� HazardZone������ tag="Hazard@{type.hazardId}@{center.q},{center.r}"��

            return false;
        }


        // Ԥ�����ѡ�ȼ��/��Һ���Ȱ�ȦӦ��
        public void ApplyHazardCircle(Hex center, int radius, HazardType type, int durationSeconds)
        {
            // TODO
        }
    }
}
