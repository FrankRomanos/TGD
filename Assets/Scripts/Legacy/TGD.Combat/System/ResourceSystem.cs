using UnityEngine;
using TGD.Data;

namespace TGD.Combat
{
    public sealed class ResourceSystem : IResourceSystem
    {
        private readonly ICombatLogger _logger;

        public ResourceSystem(ICombatLogger logger)
        {
            _logger = logger;
        }

        public void Execute(ModifyResourceOp op, RuntimeCtx ctx)
        {
            if (op == null)
                return;

            var target = op.Target ?? ctx?.Caster;
            if (target?.Stats == null)
                return;

            if (!ResourceUtility.TryGetAccessor(target.Stats, op.Resource, out var accessor) || !accessor.IsValid)
                return;

            ApplyModification(target, op.Resource, accessor, op.Amount, op.ModifyType);
        }

        private void ApplyModification(Unit unit, ResourceType resource, ResourceAccessor accessor, float rawAmount, ResourceModifyType type)
        {
            int delta = Mathf.RoundToInt(rawAmount);
            int minMax = resource == ResourceType.HP ? 1 : 0;


            switch (type)
            {
                case ResourceModifyType.Gain:
                    accessor.Current = Mathf.Clamp(accessor.Current + delta, 0, accessor.Max);
                    break;
                case ResourceModifyType.ConvertMax:
                    accessor.Max = Mathf.Max(minMax, accessor.Max + delta);
                    accessor.Current = Mathf.Clamp(accessor.Current, 0, accessor.Max);
                    break;
                case ResourceModifyType.Lock:
                    accessor.Max = Mathf.Max(minMax, accessor.Max + delta);
                    break;
                case ResourceModifyType.Overdraft:
                case ResourceModifyType.PayLate:
                    accessor.Current = Mathf.Max(0, accessor.Current - delta);
                    break;
                default:
                    accessor.Current = Mathf.Clamp(accessor.Current + delta, 0, accessor.Max);
                    break;
            }

            unit.Stats.Clamp();
            string label = resource.ToString().ToUpperInvariant();
            _logger?.Log($"RESOURCE_{label}", unit.UnitId, accessor.Current, accessor.Max);


        }
    }
}
