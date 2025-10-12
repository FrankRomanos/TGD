using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class MoveRateStatusRuntime : MonoBehaviour
    {
        [System.Serializable]
        public sealed class Entry
        {
            public string tag;
            public float mult = 1f;
            public int remainingTurns = 1;
            public bool exclusive = true;
        }

        public bool debugLog = false;
        public HexBoardTestDriver driver;
        public TurnManagerV2 turnManager;

        readonly List<Entry> _entries = new();
        readonly Dictionary<string, Entry> _entriesByTag = new();

        float _product = 1f;
        TurnManagerV2 _subscribedManager;

        Unit UnitRef => driver ? driver.UnitRef : null;

        void Awake()
        {
            ResolveAutoRefs();
            RecomputeProduct();
        }

        void OnEnable()
        {
            ResolveAutoRefs();
            AttachTurnManager(turnManager);
        }

        void OnDisable()
        {
            if (_subscribedManager != null)
            {
                _subscribedManager.TurnEnded -= OnTurnEnded;
                _subscribedManager = null;
            }
        }

        void ResolveAutoRefs()
        {
            if (driver == null)
                driver = GetComponentInParent<HexBoardTestDriver>();
            if (turnManager == null)
                turnManager = GetComponentInParent<TurnManagerV2>();
        }

        public void AttachDriver(HexBoardTestDriver drv)
        {
            driver = drv;
        }

        public void AttachTurnManager(TurnManagerV2 manager)
        {
            if (_subscribedManager == manager)
                return;

            if (_subscribedManager != null)
                _subscribedManager.TurnEnded -= OnTurnEnded;

            _subscribedManager = null;
            turnManager = manager;

            if (manager != null && isActiveAndEnabled)
            {
                manager.TurnEnded += OnTurnEnded;
                _subscribedManager = manager;
            }
        }

        void OnTurnEnded(Unit unit)
        {
            if (unit == null)
                return;
            if (UnitRef == null || unit != UnitRef)
                return;
            TickEndOfTurn(unit);
        }

        public IEnumerable<float> GetActiveMultipliers()
        {
            foreach (var entry in _entries)
            {
                if (entry == null)
                    continue;
                if (entry.remainingTurns == 0)
                    continue;
                yield return Mathf.Clamp(entry.mult, 0.01f, 100f);
            }
        }

        public float GetProduct() => _product;

        public bool HasActiveTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return false;
            if (!_entriesByTag.TryGetValue(tag, out var entry) || entry == null)
                return false;
            if (entry.remainingTurns < 0)
                return true;
            return entry.remainingTurns > 0;
        }

        public void ApplyOrRefreshExclusive(string tag, float mult, int turns, string source = null)
            => ApplyOrRefreshInternal(tag, mult, turns, true, source);

        public void ApplyStickyMultiplier(float multiplier, int turns, string source = null)
            => ApplyOrRefreshExclusive("Untyped", multiplier, turns, source);

        void ApplyOrRefreshInternal(string tag, float mult, int turns, bool exclusive, string source)
        {
            if (string.IsNullOrEmpty(tag))
                return;

            float clampedMult = Mathf.Clamp(mult, 0.01f, 100f);
            if (Mathf.Approximately(clampedMult, 1f))
            {
                Remove(tag);
                return;
            }

            int normalizedTurns = turns < 0 ? -1 : Mathf.Max(1, turns);
            var entry = GetOrCreateEntry(tag, exclusive, clampedMult, normalizedTurns, source);
            if (entry == null)
                return;

            if (exclusive)
                RemoveConflictingExclusive(entry);

            RecomputeProduct();
        }

        Entry GetOrCreateEntry(string tag, bool exclusive, float mult, int turns, string source)
        {
            if (_entriesByTag.TryGetValue(tag, out var existing) && existing != null)
            {
                existing.mult = mult;
                existing.exclusive = existing.exclusive || exclusive;
                if (turns < 0)
                    existing.remainingTurns = -1;
                else if (existing.remainingTurns < 0)
                    existing.remainingTurns = turns;
                else
                    existing.remainingTurns = Mathf.Max(existing.remainingTurns, turns);

                if (debugLog)
                    Debug.Log($"[Env] Refresh tag={tag} mult={mult:F2} turns={FormatTurns(existing.remainingTurns)}{FormatSource(source)}", this);
                return existing;
            }

            var entry = new Entry
            {
                tag = tag,
                mult = mult,
                remainingTurns = turns,
                exclusive = exclusive,
            };
            _entries.Add(entry);
            _entriesByTag[tag] = entry;

            if (debugLog)
                Debug.Log($"[Env] Apply tag={tag} mult={mult:F2} turns={FormatTurns(turns)}{FormatSource(source)}", this);

            return entry;
        }

        void RemoveConflictingExclusive(Entry sourceEntry)
        {
            if (sourceEntry == null || !sourceEntry.exclusive)
                return;

            bool sourceIsHaste = sourceEntry.mult > 1f + 1e-4f;
            bool sourceIsSlow = sourceEntry.mult < 1f - 1e-4f;
            if (!sourceIsHaste && !sourceIsSlow)
                return;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry == null || entry == sourceEntry)
                    continue;
                if (!entry.exclusive)
                    continue;

                bool entryIsHaste = entry.mult > 1f + 1e-4f;
                bool entryIsSlow = entry.mult < 1f - 1e-4f;

                if ((sourceIsHaste && entryIsHaste) || (sourceIsSlow && entryIsSlow))
                {
                    ExpireEntryAt(i);
                }
            }
        }

        void Remove(string tag)
        {
            if (!_entriesByTag.TryGetValue(tag, out var entry) || entry == null)
                return;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i] == entry)
                {
                    ExpireEntryAt(i);
                    break;
                }
            }

            RecomputeProduct();
        }

        void ExpireEntryAt(int index)
        {
            if (index < 0 || index >= _entries.Count)
                return;

            var entry = _entries[index];
            if (entry != null)
            {
                _entriesByTag.Remove(entry.tag);
                if (debugLog)
                    Debug.Log($"[Env] Expire tag={entry.tag}", this);
            }
            _entries.RemoveAt(index);
        }

        string FormatTurns(int turns)
        {
            if (turns < 0)
                return "inf";
            return turns.ToString();
        }

        string FormatSource(string source)
            => string.IsNullOrEmpty(source) ? string.Empty : $" at={source}";

        string FormatOwner(Unit unit)
            => unit != null ? unit.Id : "?";

        public void TickEndOfTurn(Unit owner)
        {
            if (_entries.Count == 0)
                return;

            bool changed = false;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry == null)
                {
                    _entries.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (entry.remainingTurns < 0)
                    continue;

                entry.remainingTurns = Mathf.Max(0, entry.remainingTurns - 1);
                if (debugLog)
                    Debug.Log($"[Env] Tick owner={FormatOwner(owner)} -> tag={entry.tag}, remain={entry.remainingTurns}", this);

                if (entry.remainingTurns == 0)
                {
                    ExpireEntryAt(i);
                    changed = true;
                }
                else
                {
                    changed = true;
                }
            }

            if (changed)
                RecomputeProduct();
        }

        void RecomputeProduct()
        {
            float product = 1f;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry == null)
                {
                    _entries.RemoveAt(i);
                    continue;
                }
                if (entry.remainingTurns == 0)
                {
                    _entriesByTag.Remove(entry.tag);
                    _entries.RemoveAt(i);
                    continue;
                }
                product *= Mathf.Clamp(entry.mult, 0.01f, 100f);
            }
            _product = Mathf.Clamp(product, 0.01f, 100f);
        }

        public void ClearAll()
        {
            _entries.Clear();
            _entriesByTag.Clear();
            _product = 1f;
        }
    }
}