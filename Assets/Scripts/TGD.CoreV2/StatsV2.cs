using System;
using UnityEngine;
using UnityEngine.Serialization;
using TGD.CoreV2.Rules;

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
        [FormerlySerializedAs("Speed")]
        [Tooltip("速度点数（序列化时 *10，便于设计 0.1 精度）")]
        public int SpeedRating;            // 蓝图/装备提供的速度点数 *10
        [Min(MoveRateRules.DefaultMinInt)]
        public int MoveRate = MoveRateRules.DefaultMinInt;           // 格/秒（底座）
        [Tooltip("Minimum allowed move rate for this unit (after all runtime modifiers).")]
        public int MoveRateMin = MoveRateRules.DefaultMinInt;
        [Tooltip("Maximum allowed move rate for this unit (after all runtime modifiers).")]
        public int MoveRateMax = MoveRateRules.DefaultMaxInt;

        [Tooltip("Base move pacing settings for the standard action.")]
        public MoveProfileV2 MoveProfile = new MoveProfileV2();

        // —— 攻击节奏 ——
        [Tooltip("Base attack pacing settings for the standard action.")]
        public AttackProfileV2 AttackProfile = new AttackProfileV2();

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

        // —— 增伤/减伤（初始系数） ——
        [Tooltip("初始增伤百分比（0 = 无额外增伤）")]
        public float DamageBonusPct = 0f;
        [Tooltip("初始减伤百分比（0 = 无额外减伤）")]
        public float DamageReducePct = 0f;

        // —— 威胁/削韧增强（加法百分比） —— 
        public float ThreatAddPct = 0f;
        public float ShredAddPct = 0f;

        // —— 派生 ——
        float SpeedRatingCurveInput => SpeedRules.DecodeBlueprintRating(Mathf.Max(0, SpeedRating));
        public float SpeedSecondsFloat => SpeedRules.MapRatingToSeconds(SpeedRatingCurveInput);
        public int SpeedSecondsInt => SpeedRules.MapRatingToSecondsInt(SpeedRatingCurveInput);
        public int Speed => SpeedSecondsInt;      // 兼容旧字段（整数秒）
        public int TurnTime => StatsMathV2.TurnTime(SpeedSecondsInt);
        public float TurnTimeFloat => StatsMathV2.BaseTurnSeconds + SpeedSecondsFloat;

        public void Clamp()
        {
            MaxHP = Mathf.Max(1, MaxHP);
            HP = Mathf.Clamp(HP, 0, MaxHP);
            MaxEnergy = Mathf.Max(0, MaxEnergy);
            Energy = Mathf.Clamp(Energy, 0, MaxEnergy);

            MoveRateMin = Mathf.Clamp(MoveRateMin, MoveRateRules.DefaultMinInt, MoveRateRules.DefaultMaxInt);
            MoveRateMax = Mathf.Clamp(MoveRateMax, MoveRateMin, MoveRateRules.DefaultMaxInt);
            MoveRate = Mathf.Clamp(MoveRate, MoveRateMin, MoveRateMax);
            if (MoveProfile == null)
                MoveProfile = new MoveProfileV2();
            MoveProfile.Clamp();
            if (AttackProfile == null)
                AttackProfile = new AttackProfileV2();
            AttackProfile.Clamp();
            // 非负保障
            PrimaryAddPct = Mathf.Max(0f, PrimaryAddPct);
            BaseCrit = Mathf.Max(0f, BaseCrit);
            CritAddPct = Mathf.Max(0f, CritAddPct);
            BaseMasteryP = Mathf.Max(0f, BaseMasteryP);
            MasteryAddPct = Mathf.Max(0f, MasteryAddPct);
            MasteryClassCoeff = Mathf.Max(0f, MasteryClassCoeff);

            DamageBonusPct = Mathf.Max(0f, DamageBonusPct);
            DamageReducePct = Mathf.Clamp(DamageReducePct, 0f, 0.95f);
        }
    }
}

