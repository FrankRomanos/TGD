using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.CoreV2.Resource
{
    [DisallowMultipleComponent]
    public sealed class UnitResourceHub : MonoBehaviour, IResourcePool
    {
        [Serializable]
        public struct SlotSpec
        {
            public string resourceId;
            public int cap;
            public int startValue;
            public bool clearOnTurnEnd;
            public bool showOnHud;
        }

        [SerializeField]
        List<SlotSpec> _defaultSlots = new List<SlotSpec>();

        readonly Dictionary<string, SlotSpec> _slotConfig = new Dictionary<string, SlotSpec>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> _values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public event Action<ResourceChangeEvent> Changed;

        public void Initialize(IEnumerable<SlotSpec> specs)
        {
            _slotConfig.Clear();
            _values.Clear();

            if (specs == null)
                return;

            foreach (var spec in specs)
            {
                if (string.IsNullOrWhiteSpace(spec.resourceId))
                    continue;

                var normalized = NormalizeSpec(spec);
                _slotConfig[normalized.resourceId] = normalized;

                var startValue = normalized.startValue;
                _values[normalized.resourceId] = startValue;
                RaiseChanged(normalized.resourceId, 0, startValue, force: true);
            }
        }

        public void InitializeFromDefault()
        {
            Initialize(_defaultSlots);
        }

        public int Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return 0;

            return _values.TryGetValue(id, out var value) ? value : 0;
        }

        public int Cap(string id)
        {
            if (string.IsNullOrEmpty(id))
                return 0;

            return _slotConfig.TryGetValue(id, out var spec) ? spec.cap : 0;
        }

        public bool TrySpend(string id, int amount)
        {
            if (string.IsNullOrEmpty(id) || amount < 0)
                return false;

            if (!_slotConfig.ContainsKey(id))
                return false;

            var current = Get(id);
            if (current < amount)
                return false;

            var after = current - amount;
            _values[id] = after;
            RaiseChanged(id, current, after);
            return true;
        }

        public void Gain(string id, int amount)
        {
            if (string.IsNullOrEmpty(id) || amount == 0)
                return;

            if (!_slotConfig.TryGetValue(id, out var spec))
                return;

            var current = Get(id);
            var after = Mathf.Clamp(current + amount, 0, spec.cap);
            if (after == current)
                return;

            _values[id] = after;
            RaiseChanged(id, current, after);
        }

        public void OnOwnerEndTurn()
        {
            foreach (var pair in _slotConfig)
            {
                if (!pair.Value.clearOnTurnEnd)
                    continue;

                var id = pair.Key;
                var current = Get(id);
                if (current == 0)
                    continue;

                _values[id] = 0;
                RaiseChanged(id, current, 0);
            }
        }

        [ContextMenu("Test Gain TestResource")]
        void EditorTestGain()
        {
            Gain("TestResource", 1);
        }

        SlotSpec NormalizeSpec(SlotSpec spec)
        {
            spec.cap = Mathf.Max(0, spec.cap);
            spec.startValue = Mathf.Clamp(spec.startValue, 0, spec.cap);
            return spec;
        }

        void RaiseChanged(string id, int before, int after, bool force = false)
        {
            if (!force && before == after)
                return;

            if (before != after)
                Debug.Log($"[Resource] {id}: {before} -> {after}", this);

            Changed?.Invoke(new ResourceChangeEvent(id, before, after));
        }
    }
}
