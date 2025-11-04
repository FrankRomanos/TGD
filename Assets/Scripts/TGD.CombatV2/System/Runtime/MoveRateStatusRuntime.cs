using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TGD.CoreV2;
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

        public Unit UnitRef => driver ? driver.UnitRef : null;

        public readonly struct EntrySnapshot
        {
            public readonly string tag;
            public readonly int remainingTurns;

            public EntrySnapshot(string tag, int remainingTurns)
            {
                this.tag = tag;
                this.remainingTurns = remainingTurns;
            }
        }

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
                _subscribedManager.UnregisterMoveRateStatus(this);
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
            if (_subscribedManager != null)
                _subscribedManager.RegisterMoveRateStatus(this);
        }

        public void AttachTurnManager(TurnManagerV2 manager)
        {
            if (_subscribedManager == manager)
                return;

            if (_subscribedManager != null)
            {
                _subscribedManager.UnregisterMoveRateStatus(this);
            }

            _subscribedManager = null;
            turnManager = manager;

            if (manager != null && isActiveAndEnabled)
            {
                manager.RegisterMoveRateStatus(this);
                _subscribedManager = manager;
            }
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
            if (turns == 0 || Mathf.Approximately(clampedMult, 1f))
                return;

            int normalizedTurns = turns < 0 ? -1 : turns;
            var entry = GetOrCreateEntry(tag, exclusive, clampedMult, normalizedTurns, source);
            if (entry == null)
                return;

            if (exclusive)
                RemoveConflictingExclusive(entry);

            RecomputeProduct();
        }

        Entry GetOrCreateEntry(string tag, bool exclusive, float mult, int turns, string source)
        {
            var unitLabel = TurnManagerV2.FormatUnitLabel(UnitRef);
            if (_entriesByTag.TryGetValue(tag, out var existing) && existing != null)
            {
                existing.mult = mult;
                existing.exclusive = existing.exclusive || exclusive;
                existing.remainingTurns = turns;

                Debug.Log($"[Sticky] Refresh U={unitLabel} tag={tag} mult={mult:F2} turns={FormatTurns(existing.remainingTurns)}", this);
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

            Debug.Log($"[Sticky] Apply U={unitLabel} tag={tag} mult={mult:F2} turns={FormatTurns(turns)}", this);

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
                    ExpireEntryAt(i, true);
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
                    ExpireEntryAt(i, true);
                    break;
                }
            }

            RecomputeProduct();
        }

        void ExpireEntryAt(int index, bool log)
        {
            if (index < 0 || index >= _entries.Count)
                return;

            var entry = _entries[index];
            if (entry != null)
            {
                _entriesByTag.Remove(entry.tag);
                if (log)
                {
                    var unitLabel = TurnManagerV2.FormatUnitLabel(UnitRef);
                    Debug.Log($"[Sticky] Expire U={unitLabel} tag={entry.tag}", this);
                }
            }
            _entries.RemoveAt(index);
        }

        string FormatTurns(int turns)
        {
            if (turns < 0)
                return "inf";
            return turns.ToString();
        }

        public void TickAll(int deltaTurns)
        {
            if (deltaTurns == 0 || _entries.Count == 0)
                return;

            bool changed = false;
            var unitLabel = TurnManagerV2.FormatUnitLabel(UnitRef);
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

                entry.remainingTurns += deltaTurns;

                if (entry.remainingTurns > 0)
                {
                    Debug.Log($"[Sticky] Tick U={unitLabel} tag={entry.tag} -> remain={entry.remainingTurns}", this);
                }
                else
                {
                    Debug.Log($"[Sticky] Expire U={unitLabel} tag={entry.tag}", this);
                    _entriesByTag.Remove(entry.tag);
                    _entries.RemoveAt(i);
                    changed = true;
                }
            }

            if (changed)
                RecomputeProduct();
        }
        public void TickOneTurn()
        {
            TickAll(-1);
        }

        bool RecomputeProduct()
        {
            float product = 1f;
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
                if (entry.remainingTurns == 0)
                {
                    _entriesByTag.Remove(entry.tag);
                    _entries.RemoveAt(i);
                    changed = true;
                    continue;
                }
                product *= Mathf.Clamp(entry.mult, 0.01f, 100f);
            }
            float clamped = Mathf.Clamp(product, 0.01f, 100f);
            if (!Mathf.Approximately(_product, clamped))
            {
                _product = clamped;
                changed = true;
            }
            return changed;
        }

        public void ClearAll()
        {
            _entries.Clear();
            _entriesByTag.Clear();
            _product = 1f;
        }

        public bool RefreshProduct()
        {
            return RecomputeProduct();
        }

        public void CopyActiveEntries(List<EntrySnapshot> buffer)
        {
            if (buffer == null)
                return;
            buffer.Clear();
            foreach (var entry in _entries)
            {
                if (entry == null)
                    continue;
                if (entry.remainingTurns == 0)
                    continue;
                buffer.Add(new EntrySnapshot(entry.tag, entry.remainingTurns));
            }
        }
        public string ActiveTagsCsv
        {
            get
            {
                if (_entries.Count == 0)
                    return "none";

                var sb = new StringBuilder();
                bool any = false;
                foreach (var entry in _entries)
                {
                    if (entry == null)
                        continue;
                    if (entry.remainingTurns == 0)
                        continue;

                    if (any)
                        sb.Append(',');

                    sb.Append(entry.tag);
                    sb.Append(':');
                    sb.Append(entry.remainingTurns < 0 ? "inf" : entry.remainingTurns.ToString());
                    any = true;
                }

                return any ? sb.ToString() : "none";
            }
        }
        public void RefreshFromTerrain(Unit unit, Hex at, float multiplier, int turns, string tagOverride = null)
        {
            if (turns == 0)
                return;

            float clampedMult = Mathf.Clamp(multiplier, 0.01f, 100f);
            if (Mathf.Approximately(clampedMult, 1f))
                return;

            string resolvedTag = !string.IsNullOrEmpty(tagOverride)
                ? tagOverride
                : $"Patch@{at.q},{at.r}";

            ApplyOrRefreshExclusive(resolvedTag, clampedMult, turns, at.ToString());
        }
    }
}