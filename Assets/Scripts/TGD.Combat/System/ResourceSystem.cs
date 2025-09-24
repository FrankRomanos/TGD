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

            float amount = op.Amount;

            switch (op.Resource)
            {
                case ResourceType.HP:
                    ModifyHp(target, amount, op.ModifyType);
                    break;
                case ResourceType.Energy:
                    ModifyEnergy(target, amount, op.ModifyType);
                    break;
                case ResourceType.posture:
                    ModifyPosture(target, amount, op.ModifyType);
                    break;
            }
        }

        private void ModifyHp(Unit unit, float amount, ResourceModifyType type)
        {
            int delta = Mathf.RoundToInt(amount);
            if (type == ResourceModifyType.Gain)
            {
                unit.Stats.HP += delta;
                if (unit.Stats.HP > unit.Stats.MaxHP)
                    unit.Stats.HP = unit.Stats.MaxHP;
            }
            else if (type == ResourceModifyType.ConvertMax)
            {
                unit.Stats.MaxHP += delta;
                if (unit.Stats.MaxHP < 1)
                    unit.Stats.MaxHP = 1;
                if (unit.Stats.HP > unit.Stats.MaxHP)
                    unit.Stats.HP = unit.Stats.MaxHP;
            }

            _logger?.Log("RESOURCE_HP", unit.UnitId, unit.Stats.HP, unit.Stats.MaxHP);
        }

        private void ModifyEnergy(Unit unit, float amount, ResourceModifyType type)
        {
            int delta = Mathf.RoundToInt(amount);
            switch (type)
            {
                case ResourceModifyType.Gain:
                    unit.Stats.Energy = Mathf.Clamp(unit.Stats.Energy + delta, 0, unit.Stats.MaxEnergy);
                    break;
                case ResourceModifyType.ConvertMax:
                    unit.Stats.MaxEnergy = Mathf.Max(0, unit.Stats.MaxEnergy + delta);
                    unit.Stats.Energy = Mathf.Clamp(unit.Stats.Energy, 0, unit.Stats.MaxEnergy);
                    break;
                default:
                    unit.Stats.Energy = Mathf.Clamp(unit.Stats.Energy + delta, 0, unit.Stats.MaxEnergy);
                    break;
            }

            _logger?.Log("RESOURCE_ENERGY", unit.UnitId, unit.Stats.Energy, unit.Stats.MaxEnergy);
        }

        private void ModifyPosture(Unit unit, float amount, ResourceModifyType type)
        {
            int delta = Mathf.RoundToInt(amount);
            if (type == ResourceModifyType.Gain)
            {
                unit.Stats.Posture = Mathf.Clamp(unit.Stats.Posture + delta, 0, unit.Stats.MaxPosture);
            }
            else if (type == ResourceModifyType.ConvertMax)
            {
                unit.Stats.MaxPosture = Mathf.Max(0, unit.Stats.MaxPosture + delta);
                unit.Stats.Posture = Mathf.Clamp(unit.Stats.Posture, 0, unit.Stats.MaxPosture);
            }

            _logger?.Log("RESOURCE_POSTURE", unit.UnitId, unit.Stats.Posture, unit.Stats.MaxPosture);
        }
    }
}
