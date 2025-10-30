using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using TGD.CombatV2;
using TGD.HexBoard;

namespace TGD.UIV2
{
    /// <summary>
    /// Drives the UI Toolkit based turn timeline HUD and animates the live queue of turn groups.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TurnTimelineController : MonoBehaviour
    {
        const float HeaderHeightFallback = 44f;
        const float SlotHeightFallback = 88f;
        const float ContentGap = 8f;

        [Header("Runtime")]
        public TurnManagerV2 turnManager;
        public CombatActionManagerV2 combatManager;
        public UIDocument document;

        [Header("Look")]
        public Sprite fallbackAvatar;
        [Min(1)] public int maxVisibleSlots = 4;
        [Min(0f)] public float scrollAnimationDuration = 0.25f;

        readonly List<TimelineEntryView> _entries = new();
        readonly List<PhaseGroup> _groups = new();

        VisualElement _contentRoot;
        TimelineEntryView _activeSlotEntry;
        Coroutine _scrollAnimation;
        bool _bonusTurnDisplayed;
        int _phaseCounter;

        static T AutoFind<T>() where T : UnityEngine.Object
        {
    #if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
    #else
            return UnityEngine.Object.FindObjectOfType<T>();
    #endif
        }

        void Awake()
        {
            if (!document)
                document = GetComponent<UIDocument>();
            if (!document)
                document = AutoFind<UIDocument>();

            if (!turnManager)
                turnManager = AutoFind<TurnManagerV2>();
            if (!combatManager)
                combatManager = AutoFind<CombatActionManagerV2>();

            InitializeRoot();
        }

        void OnEnable()
        {
            Subscribe();
            RebuildInitialState();
        }

        void OnDisable()
        {
            Unsubscribe();
            ClearAll();
        }

        void InitializeRoot()
        {
            if (!document)
                return;

            var root = document.rootVisualElement;
            if (root == null)
                return;

            _contentRoot = root.Q<VisualElement>("Content");
            if (_contentRoot != null)
                _contentRoot.Clear();
        }

        void Subscribe()
        {
            if (turnManager != null)
            {
                turnManager.PhaseBegan += OnPhaseBegan;
                turnManager.TurnStarted += OnTurnStarted;
                turnManager.TurnEnded += OnTurnEnded;
            }

            if (combatManager != null)
                combatManager.BonusTurnStateChanged += OnBonusTurnStateChanged;
        }

        void Unsubscribe()
        {
            if (turnManager != null)
            {
                turnManager.PhaseBegan -= OnPhaseBegan;
                turnManager.TurnStarted -= OnTurnStarted;
                turnManager.TurnEnded -= OnTurnEnded;
            }

            if (combatManager != null)
                combatManager.BonusTurnStateChanged -= OnBonusTurnStateChanged;
        }

        void RebuildInitialState()
        {
            ClearAll();

            if (_contentRoot == null)
                return;

            bool bonusActive = combatManager != null && combatManager.IsBonusTurnActive;
            if (bonusActive)
            {
                var bonusUnit = combatManager.CurrentBonusTurnUnit;
                bool isPlayerBonus = turnManager != null && turnManager.IsPlayerUnit(bonusUnit);
                var bonusUnits = turnManager != null ? turnManager.GetSideUnits(isPlayerBonus) : null;
                if (bonusUnits != null && bonusUnits.Count > 0)
                {
                    var bonusGroup = CreatePhaseGroup("Bonus Turn", isPlayerBonus, true, bonusUnits);
                    if (bonusGroup != null)
                    {
                        InsertGroupAtTop(bonusGroup, false);
                        _bonusTurnDisplayed = true;
                    }
                }
            }
            else
            {
                _bonusTurnDisplayed = false;
            }

            if (turnManager != null)
            {
                bool isPlayer = turnManager.IsPlayerPhase;
                int currentPhase = Mathf.Max(1, turnManager.CurrentPhaseIndex);
                var units = turnManager.GetSideUnits(isPlayer);
                if (units != null && units.Count > 0)
                {
                    var group = CreatePhaseGroup(FormatTurnLabel(currentPhase), isPlayer, false, units);
                    if (group != null)
                        InsertGroupAtTop(group, false);
                }

                _phaseCounter = currentPhase;
                FillSlots(false);
                HighlightActiveSlot(turnManager.ActiveUnit);
            }
        }

        void ClearAll()
        {
            if (_scrollAnimation != null)
            {
                StopCoroutine(_scrollAnimation);
                _scrollAnimation = null;
            }

            _entries.Clear();
            _groups.Clear();
            _activeSlotEntry = null;
            _bonusTurnDisplayed = false;

            if (_contentRoot != null)
                _contentRoot.Clear();
        }

        void OnPhaseBegan(bool isPlayerPhase)
        {
            var units = turnManager != null ? turnManager.GetSideUnits(isPlayerPhase) : null;
            if (units == null || units.Count == 0)
                return;

            int phaseIndex = turnManager != null ? Mathf.Max(1, turnManager.CurrentPhaseIndex) : ++_phaseCounter;
            if (turnManager == null)
                _phaseCounter = phaseIndex;

            var group = CreatePhaseGroup(FormatTurnLabel(phaseIndex), isPlayerPhase, false, units);
            if (group == null)
                return;

            InsertGroupAtTop(group, true);
            ProcessTimelineUpdates();
        }

        void OnTurnStarted(Unit unit)
        {
            HighlightActiveSlot(unit);
        }

        void OnTurnEnded(Unit unit)
        {
            if (unit == null)
                return;

            RemoveSlotForUnit(unit);
            ProcessTimelineUpdates();
        }

        void OnBonusTurnStateChanged()
        {
            if (combatManager == null)
                return;

            bool active = combatManager.IsBonusTurnActive;
            if (active && !_bonusTurnDisplayed)
            {
                var bonusUnit = combatManager.CurrentBonusTurnUnit;
                bool isPlayerBonus = turnManager != null && turnManager.IsPlayerUnit(bonusUnit);
                var units = turnManager != null ? turnManager.GetSideUnits(isPlayerBonus) : null;
                if (units != null && units.Count > 0)
                {
                    var group = CreatePhaseGroup("Bonus Turn", isPlayerBonus, true, units);
                    if (group != null)
                    {
                        InsertGroupAtTop(group, true);
                        _bonusTurnDisplayed = true;
                        ProcessTimelineUpdates();
                    }
                }
            }
            else if (!active)
            {
                _bonusTurnDisplayed = false;
            }
        }

        void ProcessTimelineUpdates()
        {
            FillSlots();
            if (HasAnimationWork())
                StartScrollAnimation();
            else
                FinalizeAnimationState();

            if (turnManager != null)
                HighlightActiveSlot(turnManager.ActiveUnit);
        }

        void InsertGroupAtTop(PhaseGroup group, bool animate)
        {
            if (group == null || group.headerEntry == null || _contentRoot == null)
                return;

            group.pendingRemoval = false;

            if (group.pendingUnits.Count > 0 && maxVisibleSlots > 0)
                EnsureSlotBudget(1);

            var header = group.headerEntry;
            header.isNewlyAdded = animate;
            header.root.style.opacity = 1f;
            header.root.style.translate = default;

            _groups.Insert(0, group);
            _entries.Insert(0, header);
            _contentRoot.Insert(0, header.root);
        }

        void EnsureSlotBudget(int requiredSlots)
        {
            if (requiredSlots <= 0)
                return;

            int available = maxVisibleSlots - GetActiveSlotCount();
            int needed = requiredSlots - Mathf.Max(0, available);
            while (needed > 0)
            {
                if (!MarkBottomSlotForRemoval())
                    break;
                needed--;
            }
        }

        bool MarkBottomSlotForRemoval()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry.kind != TimelineEntryKind.Slot || entry.isPendingRemoval)
                    continue;

                entry.isPendingRemoval = true;
                if (entry.group != null)
                {
                    entry.group.visibleSlots.Remove(entry);
                    if (entry.group.visibleSlots.Count == 0 && entry.group.pendingUnits.Count == 0)
                        MarkGroupForRemoval(entry.group);
                }

                if (_activeSlotEntry == entry)
                {
                    ToggleSlotActive(_activeSlotEntry, false);
                    _activeSlotEntry = null;
                }

                return true;
            }

            return false;
        }

        PhaseGroup CreatePhaseGroup(string label, bool isPlayer, bool isBonus, IReadOnlyList<Unit> units)
        {
            if (string.IsNullOrEmpty(label) || units == null || units.Count == 0)
                return null;

            var headerVisuals = CreateHeaderVisuals();
            headerVisuals.label.text = label;

            var headerEntry = new TimelineEntryView
            {
                kind = TimelineEntryKind.Header,
                root = headerVisuals.root,
                header = headerVisuals,
                isBonus = isBonus
            };

            var queue = new Queue<Unit>();
            for (int i = units.Count - 1; i >= 0; i--)
                queue.Enqueue(units[i]);

            var group = new PhaseGroup
            {
                label = label,
                isPlayer = isPlayer,
                isBonus = isBonus,
                headerEntry = headerEntry,
                pendingUnits = queue,
                visibleSlots = new List<TimelineEntryView>()
            };

            headerEntry.group = group;
            return group;
        }

        bool FillSlots(bool animate = true)
        {
            if (_contentRoot == null)
                return false;

            bool added = false;
            while (GetActiveSlotCount() < maxVisibleSlots)
            {
                var group = FindNextGroupWithPendingUnits();
                if (group == null)
                    break;

                if (group.pendingUnits.Count == 0)
                    break;

                var unit = group.pendingUnits.Dequeue();
                var entry = CreateSlotEntry(unit, group);
                entry.isNewlyAdded = animate;

                int insertIndex = GetSlotInsertIndex(group);
                _entries.Insert(insertIndex, entry);
                _contentRoot.Insert(insertIndex, entry.root);
                group.visibleSlots.Add(entry);
                added = true;
            }

            return added;
        }

        int GetSlotInsertIndex(PhaseGroup group)
        {
            int headerIndex = _entries.IndexOf(group.headerEntry);
            if (headerIndex < 0)
                return _entries.Count;
            return headerIndex + 1 + group.visibleSlots.Count;
        }

        PhaseGroup FindNextGroupWithPendingUnits()
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];
                if (group == null || group.pendingRemoval)
                    continue;
                if (group.pendingUnits.Count > 0)
                    return group;
            }

            return null;
        }

        TimelineEntryView CreateSlotEntry(Unit unit, PhaseGroup group)
        {
            var visuals = CreateSlotVisuals();
            visuals.label.style.display = DisplayStyle.None;

            var entry = new TimelineEntryView
            {
                kind = TimelineEntryKind.Slot,
                root = visuals.root,
                slot = visuals,
                group = group,
                unit = unit,
                unitId = unit != null ? unit.Id : string.Empty,
                isPlayer = group != null && group.isPlayer
            };

            ApplySlotVisuals(entry);
            return entry;
        }

        void ApplySlotVisuals(TimelineEntryView entry)
        {
            if (entry == null)
                return;

            var slot = entry.slot;
            if (slot.root == null)
                return;

            var card = slot.card;
            if (card != null)
            {
                card.RemoveFromClassList("player-turn");
                card.RemoveFromClassList("enemy-turn");
                if (entry.isPlayer)
                    card.AddToClassList("player-turn");
                else
                    card.AddToClassList("enemy-turn");

                card.RemoveFromClassList("slot-active");
            }

            var icon = slot.icon;
            if (icon != null)
            {
                var sprite = ResolveAvatar(entry.unit);
                if (sprite != null)
                    icon.style.backgroundImage = new StyleBackground(sprite);
                else if (fallbackAvatar != null)
                    icon.style.backgroundImage = new StyleBackground(fallbackAvatar);
                else
                    icon.style.backgroundImage = StyleKeyword.Null;
            }
        }

        Sprite ResolveAvatar(Unit unit)
        {
            if (unit != null && UnitAvatarRegistry.TryGetAvatar(unit, out var sprite) && sprite != null)
                return sprite;
            return fallbackAvatar;
        }

        void RemoveSlotForUnit(Unit unit)
        {
            var entry = FindSlotEntry(unit);
            if (entry == null)
                return;

            entry.isPendingRemoval = true;
            if (entry.group != null)
            {
                entry.group.visibleSlots.Remove(entry);
                if (entry.group.visibleSlots.Count == 0 && entry.group.pendingUnits.Count == 0)
                    MarkGroupForRemoval(entry.group);
            }

            if (_activeSlotEntry == entry)
            {
                ToggleSlotActive(_activeSlotEntry, false);
                _activeSlotEntry = null;
            }
        }

        void MarkGroupForRemoval(PhaseGroup group)
        {
            if (group == null || group.pendingRemoval)
                return;

            group.pendingRemoval = true;
            group.pendingUnits.Clear();
            if (group.headerEntry != null)
                group.headerEntry.isPendingRemoval = true;
        }

        TimelineEntryView FindSlotEntry(Unit unit)
        {
            if (unit == null)
                return null;

            string id = unit.Id;
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.kind != TimelineEntryKind.Slot)
                    continue;
                if (entry.isPendingRemoval)
                    continue;
                if (entry.unit == unit)
                    return entry;
                if (!string.IsNullOrEmpty(id) && string.Equals(entry.unitId, id, StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }

        void HighlightActiveSlot(Unit unit)
        {
            if (unit == null)
            {
                SetActiveSlot(null);
                return;
            }

            var entry = FindSlotEntry(unit);
            SetActiveSlot(entry);
        }

        void SetActiveSlot(TimelineEntryView entry)
        {
            if (_activeSlotEntry == entry)
                return;

            if (_activeSlotEntry != null)
                ToggleSlotActive(_activeSlotEntry, false);

            _activeSlotEntry = entry;

            if (_activeSlotEntry != null)
                ToggleSlotActive(_activeSlotEntry, true);
        }

        static void ToggleSlotActive(TimelineEntryView entry, bool active)
        {
            var card = entry != null ? entry.slot.card : null;
            card?.EnableInClassList("slot-active", active);
        }

        bool HasAnimationWork()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].isNewlyAdded || _entries[i].isPendingRemoval)
                    return true;
            }

            return false;
        }

        void StartScrollAnimation()
        {
            if (scrollAnimationDuration <= 0f)
            {
                FinalizeAnimationState();
                FillSlots();
                return;
            }

            if (_scrollAnimation != null)
            {
                StopCoroutine(_scrollAnimation);
                _scrollAnimation = null;
                FinalizeAnimationState();
                FillSlots();
            }

            if (!HasAnimationWork())
                return;

            _scrollAnimation = StartCoroutine(RunShiftAnimation());
        }

        IEnumerator RunShiftAnimation()
        {
            yield return null;

            float shift = CalculateShiftHeight();
            if (shift <= 0f)
            {
                FinalizeAnimationState();
                FillSlots();
                _scrollAnimation = null;
                yield break;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.isNewlyAdded)
                {
                    entry.root.style.translate = new Translate(0f, -shift);
                    entry.root.style.opacity = 1f;
                }
                else if (entry.isPendingRemoval)
                {
                    entry.root.style.translate = new Translate(0f, 0f);
                    entry.root.style.opacity = 1f;
                }
                else
                {
                    entry.root.style.translate = new Translate(0f, 0f);
                    entry.root.style.opacity = 1f;
                }
            }

            float elapsed = 0f;
            while (elapsed < scrollAnimationDuration)
            {
                float t = scrollAnimationDuration > 0f ? Mathf.Clamp01(elapsed / scrollAnimationDuration) : 1f;
                for (int i = 0; i < _entries.Count; i++)
                {
                    var entry = _entries[i];
                    if (entry.isNewlyAdded)
                    {
                        float offset = Mathf.Lerp(-shift, 0f, t);
                        entry.root.style.translate = new Translate(0f, offset);
                    }
                    else if (entry.isPendingRemoval)
                    {
                        float offset = Mathf.Lerp(0f, shift, t);
                        entry.root.style.translate = new Translate(0f, offset);
                        entry.root.style.opacity = Mathf.Lerp(1f, 0f, t);
                    }
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                entry.root.style.translate = new Translate(0f, 0f);
                entry.root.style.opacity = entry.isPendingRemoval ? 0f : 1f;
            }

            RemovePendingEntries();
            for (int i = 0; i < _entries.Count; i++)
            {
                _entries[i].isNewlyAdded = false;
                _entries[i].isPendingRemoval = false;
                _entries[i].root.style.opacity = 1f;
            }

            _scrollAnimation = null;
            FillSlots();
            if (HasAnimationWork())
                StartScrollAnimation();
        }

        void FinalizeAnimationState()
        {
            RemovePendingEntries();
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                entry.isNewlyAdded = false;
                entry.isPendingRemoval = false;
                entry.root.style.translate = new Translate(0f, 0f);
                entry.root.style.opacity = 1f;
            }
        }

        void RemovePendingEntries()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (!entry.isPendingRemoval)
                    continue;

                if (entry.kind == TimelineEntryKind.Slot && entry.group != null)
                    entry.group.visibleSlots.Remove(entry);

                if (entry.root.hierarchy.parent != null)
                    entry.root.RemoveFromHierarchy();

                var group = entry.group;
                _entries.RemoveAt(i);

                if (entry.kind == TimelineEntryKind.Header && group != null)
                {
                    _groups.Remove(group);
                    if (_activeSlotEntry != null && _activeSlotEntry.group == group)
                        _activeSlotEntry = null;
                }

                if (_activeSlotEntry == entry)
                    _activeSlotEntry = null;
            }
        }

        float CalculateShiftHeight()
        {
            float shift = 0f;
            int newCount = 0;
            bool hasExisting = false;

            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.isNewlyAdded)
                {
                    float height = entry.root.resolvedStyle.height;
                    if (height <= 0f)
                        height = entry.kind == TimelineEntryKind.Header ? HeaderHeightFallback : SlotHeightFallback;
                    shift += height;
                    newCount++;
                }
                else if (!entry.isPendingRemoval)
                {
                    hasExisting = true;
                }
            }

            if (newCount > 0)
            {
                int gapCount = Math.Max(0, newCount - 1);
                if (hasExisting)
                    gapCount += 1;
                shift += gapCount * ContentGap;
            }

            return shift;
        }

        int GetActiveSlotCount()
        {
            int count = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].kind == TimelineEntryKind.Slot && !_entries[i].isPendingRemoval)
                    count++;
            }

            return count;
        }

        static string FormatTurnLabel(int phaseIndex)
        {
            return $"Turn {Mathf.Max(1, phaseIndex)}";
        }

        HeaderVisuals CreateHeaderVisuals()
        {
            var root = new VisualElement();
            root.AddToClassList("turn-separator");

            var label = new Label();
            label.AddToClassList("turn-label");
            root.Add(label);

            return new HeaderVisuals
            {
                root = root,
                label = label
            };
        }

        SlotVisuals CreateSlotVisuals()
        {
            var root = new VisualElement();
            root.AddToClassList("slot-root");

            var row = new VisualElement();
            row.AddToClassList("slot-row");
            root.Add(row);

            var dragHandle = new VisualElement();
            dragHandle.AddToClassList("slot-drag-handle");
            row.Add(dragHandle);

            var card = new VisualElement();
            card.AddToClassList("slot-card");
            row.Add(card);

            var skin = new VisualElement();
            skin.AddToClassList("slot-skin");
            card.Add(skin);

            var icon = new VisualElement();
            icon.AddToClassList("slot-icon");
            skin.Add(icon);

            var frame = new VisualElement();
            frame.AddToClassList("slot-ornate-frame");
            card.Add(frame);

            var label = new Label();
            label.AddToClassList("slot-meta-label");
            row.Add(label);

            var insertMarker = new VisualElement();
            insertMarker.AddToClassList("insert-marker");
            root.Add(insertMarker);

            return new SlotVisuals
            {
                root = root,
                card = card,
                icon = icon,
                label = label
            };
        }

        enum TimelineEntryKind
        {
            Header,
            Slot
        }

        sealed class TimelineEntryView
        {
            public TimelineEntryKind kind;
            public VisualElement root;
            public PhaseGroup group;
            public bool isNewlyAdded;
            public bool isPendingRemoval;
            public bool isPlayer;
            public bool isBonus;
            public Unit unit;
            public string unitId;
            public HeaderVisuals header;
            public SlotVisuals slot;
        }

        sealed class PhaseGroup
        {
            public string label;
            public bool isPlayer;
            public bool isBonus;
            public TimelineEntryView headerEntry;
            public Queue<Unit> pendingUnits = new();
            public List<TimelineEntryView> visibleSlots = new();
            public bool pendingRemoval;
        }

        struct HeaderVisuals
        {
            public VisualElement root;
            public Label label;
        }

        struct SlotVisuals
        {
            public VisualElement root;
            public VisualElement card;
            public VisualElement icon;
            public Label label;
        }
    }
}
