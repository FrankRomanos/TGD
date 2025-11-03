using UnityEngine;

namespace TGD.CoreV2
{
    public static class StatsV2SpawnExtensions
    {
        /// 安全拷贝“出厂数值”→运行时，并清理战斗期字段
        public static void ApplyInit(this StatsV2 dst, StatsV2 init)
        {
            // —— 基础 —— 
            dst.Level = init.Level;
            dst.Attack = init.Attack;
            dst.Stamina = init.Stamina;
            dst.Armor = init.Armor;

            // —— 时间&移动 —— 
            dst.Speed = init.Speed;
            dst.MoveRate = init.MoveRate;

            // —— 资源 —— 
            dst.MaxHP = Mathf.Max(1, init.MaxHP);
            dst.HP = Mathf.Clamp(init.HP, 0, dst.MaxHP);
            dst.MaxEnergy = Mathf.Max(0, init.MaxEnergy);
            dst.Energy = Mathf.Clamp(init.Energy, 0, dst.MaxEnergy);
            dst.EnergyRegenPer2s = init.EnergyRegenPer2s;

            // —— 主属性 —— 
            dst.Strength = init.Strength;
            dst.Agility = init.Agility;
            dst.PrimaryAttr = init.PrimaryAttr;
            dst.PrimaryAddPct = 0f;      // runtime 清零

            // —— 暴击 ——
            dst.BaseCrit = init.BaseCrit;
            dst.CritRating = init.CritRating;
            dst.CritAddPct = 0f;         // runtime 清零
            dst.CritDamagePct = init.CritDamagePct;

            // —— 精通 ——
            dst.BaseMasteryP = init.BaseMasteryP;
            dst.MasteryRating = init.MasteryRating;
            dst.MasteryAddPct = 0f;      // runtime 清零
            dst.MasteryClassCoeff = init.MasteryClassCoeff;

            // —— 增伤/减伤 ——
            dst.DamageBonusPct = Mathf.Max(0f, init.DamageBonusPct);
            dst.DamageReducePct = Mathf.Clamp(init.DamageReducePct, 0f, 0.95f);
            dst.ThreatAddPct = init.ThreatAddPct;
            dst.ShredAddPct = init.ShredAddPct;

            dst.Clamp();
        }
    }
}
