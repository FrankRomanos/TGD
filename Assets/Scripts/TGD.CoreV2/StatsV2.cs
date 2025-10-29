using System;
using UnityEngine;

namespace TGD.CoreV2
{
    public enum PrimaryAttrV2 { Strength, Agility }

    [Serializable]
    public sealed class StatsV2
    {
        // —— 基础 —— 
        public int Level;
        public int Attack;
        public int Stamina;
        public int Armor;

        // —— 时间 & 移动 —— 
        public int Speed;                  // +秒/回合
        public int MoveRate = 1;           // 格/秒（底座）
        // 移速改动（由效果叠加）
        public int MoveRateFlatAdd = 0;  // 平坦加减
        public float MoveRatePctAdd = 0f; // 百分比加总（-0.5 = -50%）
        public bool IsEntangled = false; // 只有它能把速度变成 0

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

        // —— 公共资源 —— 
        public int MaxHP = 100, HP = 100;
        public int MaxEnergy = 100, Energy = 0;
        public int EnergyRegenPer2s = 0;

        // —— 主属性（两条都存，但职业只用其一） —— 
        public int Strength = 0;
        public int Agility = 0;
        public PrimaryAttrV2 PrimaryAttr = PrimaryAttrV2.Strength;
        public float PrimaryAddPct = 0f;   // 额外百分比（小数）
        public float PrimaryP
        {
            get
            {
                int rating = (PrimaryAttr == PrimaryAttrV2.Strength) ? Strength : Agility;
                return rating / 1500f + Mathf.Max(0f, PrimaryAddPct); // 15点=1%=0.01
            }
        }

        // —— 暴击 —— 
        public float BaseCrit = 0f;
        public int CritRating = 0;        // 30=1%
        public float CritAddPct = 0f;       // 额外百分比（小数）
        public int CritDamagePct = 200;   // 200% => 2.0×
        public float CritChanceRaw => StatsMathV2.CritChanceRaw(BaseCrit, CritRating, CritAddPct, 30f);
        public float CritChance => StatsMathV2.CritChanceCapped(CritChanceRaw);
        public float CritOverflow => StatsMathV2.CritOverflow(CritChanceRaw);
        public float CritMult => StatsMathV2.CritMultiplier(CritDamagePct);

        // —— 精通（可 >1） —— 
        public float BaseMasteryP = 0f;
        public int MasteryRating = 0;
        public float MasteryAddPct = 0f;
        public float MasteryClassCoeff = 1f;
        public float Mastery => StatsMathV2.MasteryValue(BaseMasteryP, MasteryRating, MasteryAddPct, 20f, MasteryClassCoeff);

        // —— 独立增伤桶（乘法相乘，桶内加法） —— 
        public float DmgBonusA_P = 0f;
        public float DmgBonusB_P = 0f;
        public float DmgBonusC_P = 0f;

        // —— 独立减伤（加法） —— 
        public float ReduceA_P = 0f;
        public float ReduceB_P = 0f;
        public float ReduceC_P = 0f;

        // —— 威胁/削韧增强（加法百分比） —— 
        public float ThreatAddPct = 0f;
        public float ShredAddPct = 0f;

        // —— 派生 —— 
        public int TurnTime => StatsMathV2.TurnTime(Speed);

        public void Clamp()
        {
            MaxHP = Mathf.Max(1, MaxHP);
            HP = Mathf.Clamp(HP, 0, MaxHP);
            MaxEnergy = Mathf.Max(0, MaxEnergy);
            Energy = Mathf.Clamp(Energy, 0, MaxEnergy);

            MoveRate = Mathf.Max(1, MoveRate);
            // 允许 MoveRatePctAdd < -1，但最终 EffectiveMoveRate 至少为1（非定身）

            // 非负保障
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

