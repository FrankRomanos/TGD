using System;
using System.Collections.Generic;
using TGD.Core;
using TGD.Data;
using UnityEngine;

namespace TGD.Combat
{
    public readonly struct ResourceAccessor
    {
        private readonly Func<int> _getCurrent;
        private readonly Action<int> _setCurrent;
        private readonly Func<int> _getMax;
        private readonly Action<int> _setMax;

        public ResourceAccessor(Func<int> getCurrent, Action<int> setCurrent, Func<int> getMax, Action<int> setMax)
        {
            _getCurrent = getCurrent;
            _setCurrent = setCurrent;
            _getMax = getMax;
            _setMax = setMax;
        }

        public bool IsValid => _getCurrent != null && _setCurrent != null && _getMax != null && _setMax != null;

        public int Current
        {
            get => _getCurrent != null ? _getCurrent() : 0;
            set => _setCurrent?.Invoke(value);
        }

        public int Max
        {
            get => _getMax != null ? _getMax() : 0;
            set => _setMax?.Invoke(value);
        }
    }

    public static class ResourceUtility
    {
        public static bool TryGetAccessor(Stats stats, ResourceType type, out ResourceAccessor accessor)
        {
            accessor = default;
            if (stats == null)
                return false;

            switch (type)
            {
                case ResourceType.HP:
                    accessor = new ResourceAccessor(() => stats.HP, v => stats.HP = v, () => stats.MaxHP, v => stats.MaxHP = v);
                    return true;
                case ResourceType.Energy:
                    accessor = new ResourceAccessor(() => stats.Energy, v => stats.Energy = v, () => stats.MaxEnergy, v => stats.MaxEnergy = v);
                    return true;
                case ResourceType.Discipline:
                    accessor = new ResourceAccessor(() => stats.Discipline, v => stats.Discipline = v, () => stats.MaxDiscipline, v => stats.MaxDiscipline = v);
                    return true;
                case ResourceType.Iron:
                    accessor = new ResourceAccessor(() => stats.Iron, v => stats.Iron = v, () => stats.MaxIron, v => stats.MaxIron = v);
                    return true;
                case ResourceType.Rage:
                    accessor = new ResourceAccessor(() => stats.Rage, v => stats.Rage = v, () => stats.MaxRage, v => stats.MaxRage = v);
                    return true;
                case ResourceType.Versatility:
                    accessor = new ResourceAccessor(() => stats.Versatility, v => stats.Versatility = v, () => stats.MaxVersatility, v => stats.MaxVersatility = v);
                    return true;
                case ResourceType.Gunpowder:
                    accessor = new ResourceAccessor(() => stats.Gunpowder, v => stats.Gunpowder = v, () => stats.MaxGunpowder, v => stats.MaxGunpowder = v);
                    return true;
                case ResourceType.point:
                    accessor = new ResourceAccessor(() => stats.Point, v => stats.Point = v, () => stats.MaxPoint, v => stats.MaxPoint = v);
                    return true;
                case ResourceType.combo:
                    accessor = new ResourceAccessor(() => stats.Combo, v => stats.Combo = v, () => stats.MaxCombo, v => stats.MaxCombo = v);
                    return true;
                case ResourceType.punch:
                    accessor = new ResourceAccessor(() => stats.Punch, v => stats.Punch = v, () => stats.MaxPunch, v => stats.MaxPunch = v);
                    return true;
                case ResourceType.qi:
                    accessor = new ResourceAccessor(() => stats.Qi, v => stats.Qi = v, () => stats.MaxQi, v => stats.MaxQi = v);
                    return true;
                case ResourceType.vision:
                    accessor = new ResourceAccessor(() => stats.Vision, v => stats.Vision = v, () => stats.MaxVision, v => stats.MaxVision = v);
                    return true;
                case ResourceType.posture:
                    accessor = new ResourceAccessor(() => stats.Posture, v => stats.Posture = v, () => stats.MaxPosture, v => stats.MaxPosture = v);
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryGetAccessor(Stats stats, CostResourceType type, out ResourceAccessor accessor)
        {
            accessor = default;
            if (type == CostResourceType.Custom)
                return false;

            if (!Enum.TryParse(type.ToString(), true, out ResourceType resourceType))
                return false;

            return TryGetAccessor(stats, resourceType, out accessor);
        }

        public static IEnumerable<ResourceType> EnumerateAvailable(Unit unit)
        {
            if (unit?.Stats == null)
                yield break;

            foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
            {
                if (TryGetAccessor(unit.Stats, type, out var accessor) && accessor.IsValid)
                    yield return type;
            }
        }

        public static void ApplyDefaults(Stats stats, ClassResourceCatalog.ClassResourceProfile profile, bool overwriteExisting)
        {
            if (stats == null)
                return;

            if (!TryGetAccessor(stats, profile.ResourceType, out var accessor) || !accessor.IsValid)
                return;

            int current = accessor.Current;
            int max = accessor.Max;

            if (overwriteExisting || max <= 0)
                accessor.Max = Mathf.Max(profile.DefaultMax, max);

            max = accessor.Max;

            if (overwriteExisting || current <= 0)
                accessor.Current = Mathf.Clamp(profile.DefaultStart, 0, max);
        }

        public static void WriteResource(Unit unit, ResourceType type, int current, int max)
        {
            if (unit?.Stats == null)
                return;

            if (!TryGetAccessor(unit.Stats, type, out var accessor) || !accessor.IsValid)
                return;

            accessor.Max = Mathf.Max(0, max);
            accessor.Current = Mathf.Clamp(current, 0, accessor.Max);
        }
    }
}
