using System.Collections.Generic;
using TGD.Combat;
using TGD.Data;
namespace TGD.Combat
{
    public interface IStatusSystem
    {
        void Execute(ApplyStatusOp op, RuntimeCtx ctx);
        void Execute(RemoveStatusOp op, RuntimeCtx ctx);
    }

    public sealed class StatusSystem : IStatusSystem
    {
        readonly ICombatEventBus _bus;
        readonly ICombatTime _time;

        public StatusSystem(ICombatEventBus bus, ICombatTime time) { _bus = bus; _time = time; }

        public void Execute(ApplyStatusOp op, RuntimeCtx ctx)
        {
            foreach (var t in op.Targets)
            {
                var inst = ApplyOrStack(t, op.StatusSkillId, op.DurationSeconds, op.StackCount,
                                        op.MaxStacks, source: op.Source);

                if (op.Accumulator != null)
                    inst.AttachExtension("acc", new StatusAccumulatorInstance(inst, op.Accumulator, _bus, _time, ctx));

                if (op.InstantOperations != null && op.InstantOperations.Count > 0)
                    EffectOpRunner.Run(op.InstantOperations, ctx);

                _bus.OnStatusApplied?.Invoke(inst);
            }
        }

        public void Execute(RemoveStatusOp op, RuntimeCtx ctx)
        {
            foreach (var t in op.Targets)
            {
                if (op.Replacement == null) { RemoveByMode(t, op); continue; }

                var old = FindFirst(t, op.StatusSkillIds);
                if (old == null) continue;

                var (stacks, remainSec, source) = ExtractTransfer(old, op.Replacement.TransferFlags);
                RemoveInstance(old);

                var neo = ApplyOrStack(t, op.Replacement.NewStatusSkillId, remainSec, stacks, 0, source);
                if (op.Replacement.ClampToNewMax) ClampToMax(neo);

                _bus.OnStatusApplied?.Invoke(neo);
                // 可选：记录 STATUS_REPLACE 日志
            }
        }

        // 下列方法按你们项目的 StatusInstance 落实现
        StatusInstance ApplyOrStack(Unit t, string id, int sec, int addStacks, int maxStacks, Unit source) { /*...*/ return null; }
        void RemoveByMode(Unit t, RemoveStatusOp op) { /*...*/ }
        StatusInstance FindFirst(Unit t, IReadOnlyList<string> ids) { /*...*/ return null; }
        (int stacks, int remainSec, Unit source) ExtractTransfer(StatusInstance s, StatusTransferFlags flags) { /*...*/ return (0, 0, null); }
        void RemoveInstance(StatusInstance s) { /*...*/ }
        void ClampToMax(StatusInstance s) { /*...*/ }
    }
}