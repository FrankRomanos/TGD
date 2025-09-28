using System;
using TGD.Data;

namespace TGD.Combat
{
    public sealed class SkillModSystem : ISkillModSystem
    {
        public void Execute(ModifySkillOp op, RuntimeCtx ctx)
        {
            // TODO：在这里把 AddCost/Multiplier 等实际挂到技能/标签上
            ctx?.Logger?.Log("SKILL_MOD_APPLY", op?.TargetSkillId, op?.ModifyType, op?.Operation, op?.ModifierType, op?.ValueExpression);
        }

        public void Execute(ReplaceSkillOp op, RuntimeCtx ctx)
        {
            if (op == null || ctx?.Caster == null)
                return;

            if (string.IsNullOrWhiteSpace(op.TargetSkillId) || string.IsNullOrWhiteSpace(op.NewSkillId))
            {
                ctx?.Logger?.Log("SKILL_REPLACE_INVALID", op?.TargetSkillId, op?.NewSkillId);
                return;
            }

            var unit = ctx.Caster;
            var skills = unit.Skills;
            if (skills == null || skills.Count == 0)
                return;

            var resolver = ctx.SkillResolver;
            var replacement = resolver?.ResolveById(op.NewSkillId) ?? SkillDatabase.GetSkillById(op.NewSkillId);
            if (replacement == null)
            {
                ctx?.Logger?.Log("SKILL_REPLACE_MISSING_DEF", op.TargetSkillId, op.NewSkillId);
                return;
            }

            int slot = -1;
            for (int i = 0; i < skills.Count; i++)
            {
                var candidate = skills[i];
                if (candidate != null && string.Equals(candidate.skillID, op.TargetSkillId, StringComparison.OrdinalIgnoreCase))
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                ctx?.Logger?.Log("SKILL_REPLACE_NOT_FOUND", op.TargetSkillId, op.NewSkillId);
                return;
            }

            int inheritedCooldown = op.InheritCooldown ? unit.GetCooldownSeconds(op.TargetSkillId) : 0;
            unit.ClearCooldown(op.TargetSkillId);

            skills[slot] = replacement;
            if (op.InheritCooldown && inheritedCooldown > 0)
                unit.SetCooldownSeconds(replacement.skillID, inheritedCooldown);

            unit.NotifySkillsChanged();
            ctx?.Logger?.Log("SKILL_REPLACE", op.TargetSkillId, "->", replacement.skillID, "inheritCd:", op.InheritCooldown);
        }
    }
}