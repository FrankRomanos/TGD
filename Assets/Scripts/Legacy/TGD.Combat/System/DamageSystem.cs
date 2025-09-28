using System;
using UnityEngine;
using TGD.Core;
using TGD.Data;

namespace TGD.Combat
{
    public sealed class DamageSystem : IDamageSystem
    {
        private readonly ICombatLogger _logger;
        private readonly ICombatEventBus _bus;
        private readonly ICombatTime _time;

        public DamageSystem(ICombatLogger logger, ICombatEventBus bus, ICombatTime time)
        {
            _logger = logger;
            _bus = bus;
            _time = time;
        }

        public void Execute(DealDamageOp op, RuntimeCtx ctx)
        {
            if (op == null)
                return;

            var source = op.Source ?? ctx?.Caster;
            var target = op.Target ?? ctx?.PrimaryTarget;
            if (target?.Stats == null)
                return;

            var attackerStats = source?.Stats;
            float baseDamage = Mathf.Max(0f, op.Amount);
            bool isCritical = DetermineCritical(op, attackerStats);

            var damageInput = new CombatFormula.DamageInput
            {
                SkillDamage = baseDamage,
                IsCritical = isCritical,
                PrimaryAttributeValue = ResolvePrimaryStat(attackerStats),
                AdditionalDamageMultiplier = attackerStats?.DamageIncrease ?? 0f,
                SituationalDamageMultiplier = attackerStats?.DamageIncreaseSecondary ?? 0f,
                DamageReduction = target.Stats.DamageReduction,
                ArmorMitigationMultiplier = ComputeArmorMitigation(attackerStats, target.Stats, op.School),
                SkillThreatMultiplier = ctx?.Skill?.threat ?? 0f,
                SkillShredMultiplier = ctx?.Skill?.shredMultiplier ?? 0f,
                MasteryConversionRatio = ResolveMasteryRatio(source)
            };

            var result = CombatFormula.CalculateDamage(damageInput, attackerStats ?? new Stats());
            float finalDamage = result.Damage;
            finalDamage *= Mathf.Max(0.01f, target.Stats.DamageTakenMultiplier);

            ApplyDamage(target, finalDamage);

            _logger?.Log("DAMAGE", source?.UnitId ?? "SYSTEM", target.UnitId, finalDamage, op.School, isCritical ? "CRIT" : "NORMAL");
            _bus?.EmitDamageResolved(source, target, finalDamage, false, op.School, _time?.Now ?? 0f);
        }

        public void Execute(HealOp op, RuntimeCtx ctx)
        {
            if (op == null)
                return;

            var source = op.Source ?? ctx?.Caster;
            var target = op.Target ?? ctx?.PrimaryTarget;
            if (target?.Stats == null)
                return;

            float baseHeal = Mathf.Max(0f, op.Amount);
            bool isCritical = DetermineCritical(op, source?.Stats);
            float multiplier = 1f + (source?.Stats?.HealIncrease ?? 0f);
            if (isCritical)
                multiplier *= 2f + (source?.Stats?.CritDamage ?? 0f) / 100f;

            float healAmount = baseHeal * multiplier;
            healAmount = Mathf.Max(0f, healAmount);

            target.Stats.HP += Mathf.RoundToInt(healAmount);
            target.Stats.Clamp();

            _logger?.Log("HEAL", source?.UnitId ?? "SYSTEM", target.UnitId, healAmount, isCritical ? "CRIT" : "NORMAL");
        }

        private static void ApplyDamage(Unit target, float amount)
        {
            if (target?.Stats == null)
                return;

            int damage = Mathf.RoundToInt(amount);
            if (damage <= 0)
                return;

            if (target.Stats.Shield > 0)
            {
                int absorbed = Mathf.Min(target.Stats.Shield, damage);
                target.Stats.Shield -= absorbed;
                damage -= absorbed;
            }

            if (damage <= 0)
                return;

            target.Stats.HP -= damage;
            if (target.Stats.HP < 0)
                target.Stats.HP = 0;
        }

        private static bool DetermineCritical(DealDamageOp op, Stats attacker)
        {
            if (!op.CanCrit || attacker == null)
                return false;

            float critChance = Mathf.Clamp01(attacker.Crit);
            return UnityEngine.Random.value < critChance;
        }

        private static bool DetermineCritical(HealOp op, Stats caster)
        {
            if (!op.CanCrit || caster == null)
                return false;

            float critChance = Mathf.Clamp01(caster.Crit);
            return UnityEngine.Random.value < critChance;
        }

        private static float ResolvePrimaryStat(Stats stats)
        {
            if (stats == null)
                return 0f;
            return Mathf.Max(stats.Strength, stats.Agility, stats.Attack, stats.SpellPower);
        }

        private static float ResolveMasteryRatio(Unit unit)
        {
            if (unit?.Skills == null)
                return 1f;

            foreach (var skill in unit.Skills)
            {
                if (skill == null)
                    continue;
                if (skill.skillType == SkillType.Mastery && skill.masteryStatConversionRatio > 0f)
                    return skill.masteryStatConversionRatio;
            }

            return 1f;
        }

        private static float ComputeArmorMitigation(Stats attacker, Stats defender, DamageSchool school)
        {
            if (defender == null)
                return 1f;

            int armor = Mathf.Max(0, defender.Armor);
            if (attacker != null)
                armor = Mathf.Max(0, armor - attacker.ArmorPenetration);

            const float armorScaling = 120f;
            float reduction = armor / (armor + armorScaling);
            reduction = Mathf.Clamp01(reduction);

            switch (school)
            {
                case DamageSchool.Magical:
                case DamageSchool.Fire:
                case DamageSchool.Frost:
                    // Magical schools rely a bit more on mitigation.
                    reduction *= 0.9f;
                    break;
            }

            return 1f - reduction;
        }
    }
}
