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
            // TODO：在这里完成技能替换（可通过 ctx.SkillResolver 或 Unit 的技能表）
            ctx?.Logger?.Log("SKILL_REPLACE", op?.TargetSkillId, "->", op?.NewSkillId, "inheritCd:", op?.InheritCooldown);
        }
    }
}