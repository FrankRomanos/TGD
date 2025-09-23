namespace TGD.Combat
{
    public sealed class SkillModSystem : ISkillModSystem
    {
        public void Execute(ModifySkillOp op, RuntimeCtx ctx)
        {
            // TODO��������� AddCost/Multiplier ��ʵ�ʹҵ�����/��ǩ��
            ctx?.Logger?.Log("SKILL_MOD_APPLY", op?.TargetSkillId, op?.ModifyType, op?.Operation, op?.ModifierType, op?.ValueExpression);
        }

        public void Execute(ReplaceSkillOp op, RuntimeCtx ctx)
        {
            // TODO����������ɼ����滻����ͨ�� ctx.SkillResolver �� Unit �ļ��ܱ�
            ctx?.Logger?.Log("SKILL_REPLACE", op?.TargetSkillId, "->", op?.NewSkillId, "inheritCd:", op?.InheritCooldown);
        }
    }
}