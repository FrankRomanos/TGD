using System;
using System.Collections.Generic;
using System.Linq;
using TGD.Data;

namespace TGD.Combat
{
    public sealed class StatusSystem : IStatusSystem
    {
        readonly ICombatEventBus _bus;
        readonly ICombatTime _time;
        readonly ICombatLogger _logger;
        readonly Dictionary<Unit, List<StatusInstance>> _active = new();

        public StatusSystem(ICombatEventBus bus, ICombatTime time, ICombatLogger logger)
        {
            _bus = bus;
            _time = time;
            _logger = logger;
        }

        public void Execute(ApplyStatusOp op, RuntimeCtx ctx)
        {
            if (op == null)
                return;

            foreach (var target in op.Targets ?? Array.Empty<Unit>())
            {
                var instance = ApplyOrStack(target, op, ctx);
                if (instance == null)
                    continue;

                if (op.Accumulator != null)
                    instance.AttachExtension("acc", new StatusAccumulatorInstance(instance, op.Accumulator, _bus, _time, ctx));

                if (op.InstantOperations != null && op.InstantOperations.Count > 0)
                    EffectOpRunner.Run(op.InstantOperations, ctx);

                EmitApplied(instance);
            }
        }

        public void Execute(RemoveStatusOp op, RuntimeCtx ctx)
        {
            if (op == null)
                return;

            foreach (var target in op.Targets ?? Array.Empty<Unit>())
            {
                if (op.Replacement == null)
                {
                    RemoveByMode(target, op);
                    continue;
                }

                var status = FindFirst(target, op.StatusSkillIds);
                if (status == null)
                    continue;

                var (stacks, remain, source) = ExtractTransfer(status, op.Replacement.TransferFlags);
                RemoveStatus(target, status, StatusRemovalReason.Replace);

                var replacement = new ApplyStatusOp
                {
                    Source = source ?? status.Source,
                    Targets = new[] { target },
                    StatusSkillId = op.Replacement.NewStatusSkillId,
                    DurationSeconds = remain,
                    StackCount = stacks == 0 ? op.StackCount : stacks,
                    MaxStacks = op.MaxStacks,
                    IsPermanent = remain < 0,
                    IsInstant = remain == 0
                };

                var newStatus = ApplyOrStack(target, replacement, ctx);
                if (newStatus != null && op.Replacement.ClampToNewMax)
                    ClampToMax(newStatus);

                if (newStatus != null)
                {
                    EmitApplied(newStatus);
                    _logger?.Log("STATUS_REPLACE", target?.UnitId, status.StatusSkillId, op.Replacement.NewStatusSkillId);
                }
            }
        }

        public void Tick(Unit unit, int deltaSeconds)
        {
            if (unit == null || deltaSeconds <= 0)
                return;

            if (!_active.TryGetValue(unit, out var list) || list.Count == 0)
                return;

            var expired = new List<StatusInstance>();
            foreach (var status in list.ToList())
            {
                if (status.Tick(deltaSeconds, out _))
                    expired.Add(status);
            }

            foreach (var status in expired)
                RemoveStatus(unit, status, StatusRemovalReason.Expire);
        }

        StatusInstance ApplyOrStack(Unit target, ApplyStatusOp op, RuntimeCtx ctx)
        {
            if (target == null || string.IsNullOrWhiteSpace(op?.StatusSkillId))
                return null;

            var list = GetList(target);
            var instance = list.FirstOrDefault(s => string.Equals(s.StatusSkillId, op.StatusSkillId, StringComparison.OrdinalIgnoreCase));

            if (instance == null)
            {
                var statusSkill = ResolveStatusSkill(op.StatusSkillId, target, ctx);
                instance = new StatusInstance(target, op.Source, statusSkill, op.IsPermanent, op.IsInstant, op.MaxStacks);
                instance.SetMaxStacks(op.MaxStacks);
                list.Add(instance);
                target.AddStatus(instance);
            }
            else
            {
                instance.SetSource(op.Source ?? instance.Source);
                if (op.MaxStacks > 0)
                    instance.SetMaxStacks(op.MaxStacks);
            }

            instance.AddStacks(op.StackCount, op.DurationSeconds);
            return instance;
        }

        void RemoveByMode(Unit target, RemoveStatusOp op)
        {
            var matches = GetMatches(target, op.StatusSkillIds).ToList();
            if (matches.Count == 0)
                return;

            foreach (var status in matches)
            {
                switch (op.RemovalMode)
                {
                    case StatusRemovalMode.RemoveAllStacks:
                    case StatusRemovalMode.RemoveMatching:
                        RemoveStatus(target, status, StatusRemovalReason.Dispel);
                        break;
                    case StatusRemovalMode.RemoveSpecificStacks:
                        int count = op.StackCount <= 0 ? 1 : op.StackCount;
                        status.RemoveStacks(count);
                        if (status.IsEmpty)
                            RemoveStatus(target, status, StatusRemovalReason.Dispel);
                        break;
                }
            }
        }

        IReadOnlyList<StatusInstance> GetMatches(Unit target, IReadOnlyList<string> ids)
        {
            if (target == null)
                return Array.Empty<StatusInstance>();

            if (!_active.TryGetValue(target, out var list) || list.Count == 0)
                return Array.Empty<StatusInstance>();

            if (ids == null || ids.Count == 0)
                return list.ToList();

            var set = new HashSet<string>(ids.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
            if (set.Count == 0)
                return list.ToList();

            return list.Where(s => set.Contains(s.StatusSkillId)).ToList();
        }

        StatusInstance FindFirst(Unit target, IReadOnlyList<string> ids)
        {
            return GetMatches(target, ids).FirstOrDefault();
        }

        (int stacks, int remainSec, Unit source) ExtractTransfer(StatusInstance status, StatusTransferFlags flags)
        {
            int stacks = flags.HasFlag(StatusTransferFlags.Stacks) ? status.StackCount : 0;
            int remain = flags.HasFlag(StatusTransferFlags.Duration) ? status.GetRemainingSecondsRounded() : 0;
            Unit source = flags.HasFlag(StatusTransferFlags.Source) ? status.Source : null;
            return (stacks, remain, source);
        }

        void RemoveStatus(Unit target, StatusInstance status, StatusRemovalReason reason)
        {
            if (target == null || status == null)
                return;

            if (!_active.TryGetValue(target, out var list))
                return;

            list.Remove(status);
            target.RemoveStatus(status);
            if (list.Count == 0)
                _active.Remove(target);

            switch (reason)
            {
                case StatusRemovalReason.Expire:
                    status.InvokeExpire();
                    _bus?.EmitStatusExpired(new StatusEvent(target, status.Source, status.StatusSkillId, 0, 0));
                    _logger?.Log("STATUS_EXPIRE", target.UnitId, status.StatusSkillId);
                    break;
                case StatusRemovalReason.Dispel:
                    status.InvokeDispel();
                    _bus?.EmitStatusDispelled(new StatusEvent(target, status.Source, status.StatusSkillId, 0, 0));
                    _logger?.Log("STATUS_EXPIRE", target.UnitId, status.StatusSkillId, "dispel");
                    break;
                case StatusRemovalReason.Replace:
                    status.InvokeDispel();
                    _bus?.EmitStatusDispelled(new StatusEvent(target, status.Source, status.StatusSkillId, 0, 0));
                    break;
            }
        }

        void EmitApplied(StatusInstance instance)
        {
            if (instance?.Target == null)
                return;

            var evt = new StatusEvent(instance.Target, instance.Source, instance.StatusSkillId, instance.StackCount, instance.GetRemainingSecondsRounded());
            _bus?.EmitStatusApplied(evt);
            _logger?.Log("STATUS_APPLY", instance.Target.UnitId, instance.StatusSkillId, instance.StackCount);
        }

        void ClampToMax(StatusInstance status)
        {
            if (status == null)
                return;

            if (status.MaxStacks > 0 && status.StackCount > status.MaxStacks)
            {
                int remove = status.StackCount - status.MaxStacks;
                status.RemoveStacks(remove);
            }
        }

        List<StatusInstance> GetList(Unit unit)
        {
            if (unit == null)
                return new List<StatusInstance>();

            if (!_active.TryGetValue(unit, out var list))
            {
                list = new List<StatusInstance>();
                _active[unit] = list;
            }

            return list;
        }

        SkillDefinition ResolveStatusSkill(string skillId, Unit target, RuntimeCtx ctx)
        {
            return ctx?.SkillResolver?.ResolveById(skillId)
                   ?? ctx?.Caster?.FindSkill(skillId)
                   ?? target?.FindSkill(skillId);
        }

        enum StatusRemovalReason
        {
            Expire,
            Dispel,
            Replace
        }
    }
}
