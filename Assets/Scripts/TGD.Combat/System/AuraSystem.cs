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
            // ��������һ��
            ApplyAdditionalOpsToFiltered(op, anchor, ctx);

            // �� DurationSeconds > 0����ע�����������ƶ��¼�ά��
            // �򻯰汾��һ����Ӧ�ã����ڡ���Ŀ��Ϊê�԰뾶���Ѿ� ApplyStatus����
        }

        void ApplyAdditionalOpsToFiltered(AuraOp op, Unit anchor, RuntimeCtx ctx)
        {
            var targets = QueryByRange(anchor, op); // ���� RangeMode/Radius/MinMaxRadius ����
                                                    // �� AdditionalOperations �е� ApplyStatus �� targets ��Ч����ͨ����¡ op���� Targets ��д��
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