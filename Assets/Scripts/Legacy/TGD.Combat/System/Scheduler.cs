using System;

namespace TGD.Combat
{
    public sealed class Scheduler : IScheduler
    {
        readonly Random _rng = new Random(12345); // TODO: ע��ս������

        public void Execute(ScheduleOp op, RuntimeCtx ctx)
        {
            if (op == null || ctx == null) return;

            switch (op.Kind)
            {
                case ScheduleKind.RandomOutcome:
                    int rolls = Math.Max(1, op.RepeatCount);
                    for (int i = 0; i < rolls; i++)
                    {
                        var picked = PickByWeight(op.Options);
                        if (picked?.Operations != null)
                            EffectOpRunner.Run(picked.Operations, ctx);
                    }
                    break;

                case ScheduleKind.Repeat:
                    for (int i = 0; i < Math.Max(1, op.RepeatCount); i++)
                        EffectOpRunner.Run(op.Operations, ctx);
                    break;

                default:
                    // ��ֱ��ִ�У�����������ʱ/������
                    EffectOpRunner.Run(op.Operations, ctx);
                    break;
            }
        }

        private ScheduleOption PickByWeight(System.Collections.Generic.IReadOnlyList<ScheduleOption> options)
        {
            if (options == null || options.Count == 0) return null;
            int total = 0;
            foreach (var o in options) total += Math.Max(1, o.Weight);
            int r = _rng.Next(0, total);
            int acc = 0;
            foreach (var o in options)
            {
                acc += Math.Max(1, o.Weight);
                if (r < acc) return o;
            }
            return options[0];
        }
    }
}
