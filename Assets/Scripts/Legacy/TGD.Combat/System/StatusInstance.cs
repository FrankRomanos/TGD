using System;
using System.Collections.Generic;
using System.Linq;
using TGD.Data;

namespace TGD.Combat
{
    public sealed class StatusInstance
    {
        readonly List<float> _stackDurations = new();
        readonly Dictionary<string, object> _extensions = new(StringComparer.OrdinalIgnoreCase);

        public StatusInstance(Unit target, Unit source, SkillDefinition statusSkill, bool isPermanent, bool isInstant, int maxStacks)
        {
            Target = target;
            Source = source;
            StatusSkill = statusSkill;
            StatusSkillId = statusSkill?.skillID ?? string.Empty;
            IsPermanent = isPermanent;
            IsInstant = isInstant;
            MaxStacks = Math.Max(0, maxStacks);
        }

        public event Action OnExpire;
        public event Action OnDispel;

        public Unit Target { get; }
        public Unit Source { get; private set; }
        public SkillDefinition StatusSkill { get; }
        public string StatusSkillId { get; }
        public bool IsPermanent { get; }
        public bool IsInstant { get; }
        public int MaxStacks { get; private set; }
        public int StackCount => _stackDurations.Count;
        public IReadOnlyList<float> StackDurations => _stackDurations;
        public bool IsEmpty => !IsPermanent && _stackDurations.Count == 0;
        public float RemainingSeconds => IsPermanent ? float.PositiveInfinity : (_stackDurations.Count == 0 ? 0f : _stackDurations.Max());

        public void SetSource(Unit source)
        {
            if (source != null)
                Source = source;
        }

        public void SetMaxStacks(int maxStacks)
        {
            MaxStacks = Math.Max(0, maxStacks);
            if (MaxStacks > 0 && _stackDurations.Count > MaxStacks)
            {
                _stackDurations.Sort();
                while (_stackDurations.Count > MaxStacks)
                    _stackDurations.RemoveAt(0);
            }
        }

        public void AddStacks(int stackCount, int durationSeconds)
        {
            if (stackCount <= 0)
                stackCount = 1;

            float duration = ResolveDuration(durationSeconds);
            for (int i = 0; i < stackCount; i++)
            {
                if (MaxStacks > 0 && _stackDurations.Count >= MaxStacks)
                    RemoveOldestStack();
                _stackDurations.Add(duration);
            }
        }

        float ResolveDuration(int durationSeconds)
        {
            if (IsPermanent)
                return float.PositiveInfinity;
            if (durationSeconds < 0)
                return 0f;
            return durationSeconds;
        }

        void RemoveOldestStack()
        {
            if (_stackDurations.Count == 0)
                return;

            int index = 0;
            float smallest = _stackDurations[0];
            for (int i = 1; i < _stackDurations.Count; i++)
            {
                if (_stackDurations[i] < smallest)
                {
                    smallest = _stackDurations[i];
                    index = i;
                }
            }

            _stackDurations.RemoveAt(index);
        }

        public bool Tick(int deltaSeconds, out int expiredStacks)
        {
            expiredStacks = 0;
            if (IsPermanent || deltaSeconds <= 0)
                return false;

            bool removedAny = false;
            for (int i = _stackDurations.Count - 1; i >= 0; i--)
            {
                float value = _stackDurations[i];
                if (float.IsPositiveInfinity(value))
                    continue;

                value -= deltaSeconds;
                if (value <= 0f)
                {
                    _stackDurations.RemoveAt(i);
                    expiredStacks++;
                    removedAny = true;
                }
                else
                {
                    _stackDurations[i] = value;
                }
            }

            return removedAny && _stackDurations.Count == 0;
        }

        public int RemoveStacks(int count)
        {
            if (count <= 0)
                count = 1;

            int removed = 0;
            for (int i = 0; i < count && _stackDurations.Count > 0; i++)
            {
                _stackDurations.RemoveAt(_stackDurations.Count - 1);
                removed++;
            }

            return removed;
        }

        public void RemoveAllStacks()
        {
            _stackDurations.Clear();
        }

        public void AttachExtension(string key, object extension)
        {
            if (string.IsNullOrWhiteSpace(key) || extension == null)
                return;
            _extensions[key] = extension;
        }

        public T GetExtension<T>(string key) where T : class
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;
            return _extensions.TryGetValue(key, out var value) ? value as T : null;
        }

        public EffectContext BuildEffectContext()
        {
            var caster = Source ?? Target;
            var ctx = new EffectContext(caster, StatusSkill ?? caster?.FindSkill(StatusSkillId));
            ctx.PrimaryTarget = Target;

            if (Source != null && Target != null)
            {
                if (Source.IsAllyOf(Target))
                {
                    if (!ctx.Allies.Contains(Target))
                        ctx.Allies.Add(Target);
                }
                else
                {
                    if (!ctx.Enemies.Contains(Target))
                        ctx.Enemies.Add(Target);
                }
            }

            return ctx;
        }

        public void InvokeExpire()
        {
            OnExpire?.Invoke();
        }

        public void InvokeDispel()
        {
            OnDispel?.Invoke();
        }

        public int GetRemainingSecondsRounded()
        {
            if (IsPermanent)
                return -1;
            return (int)Math.Ceiling(RemainingSeconds);
        }
    }
}
