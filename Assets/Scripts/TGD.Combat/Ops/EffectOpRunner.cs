// TGD.Combat/EffectOpRunner.cs
using System.Collections.Generic;

namespace TGD.Combat
{
    public static class EffectOpRunner
    {
        public static void Run(IReadOnlyList<EffectOp> ops, RuntimeCtx ctx)
        {
            if (ops == null || ctx == null) return;

            foreach (var op in ops)
            {
                switch (op.Type)
                {
                    case EffectOpType.DealDamage: ctx.DamageSystem.Execute((DealDamageOp)op, ctx); break;
                    case EffectOpType.Heal: ctx.DamageSystem.Execute((HealOp)op, ctx); break;
                    case EffectOpType.ModifyResource: ctx.ResourceSystem.Execute((ModifyResourceOp)op, ctx); break;
                    case EffectOpType.ApplyStatus: ctx.StatusSystem.Execute((ApplyStatusOp)op, ctx); break;
                    case EffectOpType.RemoveStatus: ctx.StatusSystem.Execute((RemoveStatusOp)op, ctx); break;
                    case EffectOpType.ModifyCooldown: ctx.CooldownSystem.Execute((ModifyCooldownOp)op, ctx); break;
                    case EffectOpType.ModifySkill: ctx.SkillModSystem.Execute((ModifySkillOp)op, ctx); break;
                    case EffectOpType.ReplaceSkill: ctx.SkillModSystem.Execute((ReplaceSkillOp)op, ctx); break;
                    case EffectOpType.Move: ctx.MovementSystem.Execute((MoveOp)op, ctx); break;
                    case EffectOpType.SpawnAura: ctx.AuraSystem.Execute((AuraOp)op, ctx); break;
                    case EffectOpType.Schedule: ctx.Scheduler.Execute((ScheduleOp)op, ctx); break;
                    case EffectOpType.Log: ctx.Logger.Emit(op as LogOp, ctx); break;
                }
            }
        }
    }
}
