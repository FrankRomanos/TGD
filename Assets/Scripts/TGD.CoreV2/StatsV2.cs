using System;
using UnityEngine;

namespace TGD.CoreV2
{
    public enum PrimaryAttrV2 { Strength, Agility }

    [Serializable]
    public sealed class StatsV2
    {
        // ���� ���� ���� 
        public int Level;
        public int Attack;
        public int Stamina;
        public int Armor;

        // ���� ʱ�� & �ƶ� ���� 
        public int Speed;                  // +��/�غ�
        public int MoveRate = 1;           // ��/�루������
        // ���ٸĶ�����Ч�����ӣ�
        public int MoveRateFlatAdd = 0;  // ƽ̹�Ӽ�
        public float MoveRatePctAdd = 0f; // �ٷֱȼ��ܣ�-0.5 = -50%��
        public bool IsEntangled = false; // ֻ�����ܰ��ٶȱ�� 0

        public int EffectiveMoveRate
        {
            get
            {
                if (IsEntangled) return 0;
                float baseR = Mathf.Max(1, MoveRate + MoveRateFlatAdd);
                float afterPct = baseR * (1f + MoveRatePctAdd);
                int r = Mathf.FloorToInt(afterPct);
                return Mathf.Max(1, r);
            }
        }

        // ���� ������Դ ���� 
        public int MaxHP = 100, HP = 100;
        public int MaxEnergy = 100, Energy = 0;
        public int EnergyRegenPer2s = 0;

        // ���� �����ԣ��������棬��ְҵֻ����һ�� ���� 
        public int Strength = 0;
        public int Agility = 0;
        public PrimaryAttrV2 PrimaryAttr = PrimaryAttrV2.Strength;
        public float PrimaryAddPct = 0f;   // ����ٷֱȣ�С����
        public float PrimaryP
        {
            get
            {
                int rating = (PrimaryAttr == PrimaryAttrV2.Strength) ? Strength : Agility;
                return rating / 1500f + Mathf.Max(0f, PrimaryAddPct); // 15��=1%=0.01
            }
        }

        // ���� ���� ���� 
        public float BaseCrit = 0f;
        public int CritRating = 0;        // 30=1%
        public float CritAddPct = 0f;       // ����ٷֱȣ�С����
        public int CritDamagePct = 200;   // 200% => 2.0��
        public float CritChanceRaw => StatsMathV2.CritChanceRaw(BaseCrit, CritRating, CritAddPct, 30f);
        public float CritChance => StatsMathV2.CritChanceCapped(CritChanceRaw);
        public float CritOverflow => StatsMathV2.CritOverflow(CritChanceRaw);
        public float CritMult => StatsMathV2.CritMultiplier(CritDamagePct);

        // ���� ��ͨ���� >1�� ���� 
        public float BaseMasteryP = 0f;
        public int MasteryRating = 0;
        public float MasteryAddPct = 0f;
        public float MasteryClassCoeff = 1f;
        public float Mastery => StatsMathV2.MasteryValue(BaseMasteryP, MasteryRating, MasteryAddPct, 20f, MasteryClassCoeff);

        // ���� ��������Ͱ���˷���ˣ�Ͱ�ڼӷ��� ���� 
        public float DmgBonusA_P = 0f;
        public float DmgBonusB_P = 0f;
        public float DmgBonusC_P = 0f;

        // ���� �������ˣ��ӷ��� ���� 
        public float ReduceA_P = 0f;
        public float ReduceB_P = 0f;
        public float ReduceC_P = 0f;

        // ���� ��в/������ǿ���ӷ��ٷֱȣ� ���� 
        public float ThreatAddPct = 0f;
        public float ShredAddPct = 0f;

        // ���� ���� ���� 
        public int TurnTime => StatsMathV2.TurnTime(Speed);

        public void Clamp()
        {
            MaxHP = Mathf.Max(1, MaxHP);
            HP = Mathf.Clamp(HP, 0, MaxHP);
            MaxEnergy = Mathf.Max(0, MaxEnergy);
            Energy = Mathf.Clamp(Energy, 0, MaxEnergy);

            MoveRate = Mathf.Max(1, MoveRate);
            // ���� MoveRatePctAdd < -1�������� EffectiveMoveRate ����Ϊ1���Ƕ���

            // �Ǹ�����
            PrimaryAddPct = Mathf.Max(0f, PrimaryAddPct);
            BaseCrit = Mathf.Max(0f, BaseCrit);
            CritAddPct = Mathf.Max(0f, CritAddPct);
            BaseMasteryP = Mathf.Max(0f, BaseMasteryP);
            MasteryAddPct = Mathf.Max(0f, MasteryAddPct);
            MasteryClassCoeff = Mathf.Max(0f, MasteryClassCoeff);

            DmgBonusA_P = Mathf.Max(0f, DmgBonusA_P);
            DmgBonusB_P = Mathf.Max(0f, DmgBonusB_P);
            DmgBonusC_P = Mathf.Max(0f, DmgBonusC_P);

            ReduceA_P = Mathf.Max(0f, ReduceA_P);
            ReduceB_P = Mathf.Max(0f, ReduceB_P);
            ReduceC_P = Mathf.Max(0f, ReduceC_P);
        }
    }
}

