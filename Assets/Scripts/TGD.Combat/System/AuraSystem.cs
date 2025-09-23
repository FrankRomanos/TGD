using TGD.Combat;

namespace TGD.Combat
{
    public interface IAuraSystem { void Execute(AuraOp op, RuntimeCtx ctx); }

    public sealed class AuraSystem : IAuraSystem
    {
        readonly ICombatEventBus _bus;
        public AuraSystem(ICombatEventBus bus) { _bus = bus; }
        public void Execute(AuraOp op, RuntimeCtx ctx)
        {
            var anchor = op.AnchorUnit ?? ctx.Caster;
            // 立即评估一次
            ApplyAdditionalOpsToFiltered(op, anchor, ctx);

            // 若 DurationSeconds > 0，可注册心跳或订阅移动事件维持
            // 简化版本：一次性应用（用于“以目标为锚对半径内友军 ApplyStatus”）
        }

        void ApplyAdditionalOpsToFiltered(AuraOp op, Unit anchor, RuntimeCtx ctx)
        {
            var targets = QueryByRange(anchor, op); // 根据 RangeMode/Radius/MinMaxRadius 过滤
                                                    // 用 AdditionalOperations 中的 ApplyStatus 对 targets 生效（可通过克隆 op，将 Targets 改写）
            foreach (var nested in op.AdditionalOperations)
            {
                if (nested is ApplyStatusOp a)
                    EffectOpRunner.Run(new[] { a with { Targets = targets } }, ctx);
                else
                    EffectOpRunner.Run(new[] { nested }, ctx);
            }
        }
    }
}