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
        bool _nextPhaseIsPlayer;

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
                    var bonusGroup = CreatePhaseGroup(-1, "Bonus Turn", isPlayerBonus, true, bonusUnits, false);
                    if (bonusGroup != null)
                    {
                        MoveGroupToBottom(bonusGroup, false);
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
                    var group = CreatePhaseGroup(currentPhase, FormatTurnLabel(currentPhase), isPlayer, false, units, false);
                    if (group != null)
                        MoveGroupToBottom(group, false);
                }

                _phaseCounter = currentPhase;
                _nextPhaseIsPlayer = !isPlayer;
                EnsureFutureProjections();
                var desiredCounts = ComputeDesiredSlotCounts();
                TrimExcessSlots(desiredCounts);
                FillSlots(desiredCounts, false);
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
            _phaseCounter = Math.Max(_phaseCounter, phaseIndex);

            var existing = FindPhaseGroup(phaseIndex, isPlayerPhase);
            if (existing != null)
            {
                RefreshGroupUnits(existing, units);
                MoveGroupToBottom(existing, true);
            }
            else
            {
                var group = CreatePhaseGroup(phaseIndex, FormatTurnLabel(phaseIndex), isPlayerPhase, false, units, false);
                if (group == null)
                    return;
                MoveGroupToBottom(group, true);
            }

            _nextPhaseIsPlayer = !isPlayerPhase;
            EnsureFutureProjections();
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
            if (active)
            {
                var bonusUnit = combatManager.CurrentBonusTurnUnit;
                bool isPlayerBonus = turnManager != null && turnManager.IsPlayerUnit(bonusUnit);
                var units = turnManager != null ? turnManager.GetSideUnits(isPlayerBonus) : null;
                if (units == null || units.Count == 0)
                    return;

                const string label = "Bonus Turn";
                var existing = FindBonusGroup(isPlayerBonus);
                if (existing != null)
                {
                    existing.phaseIndex = -1;
                    RefreshGroupUnits(existing, units, label, false);
                    MoveGroupToBottom(existing, true);
                }
                else
                {
                    var group = CreatePhaseGroup(-1, label, isPlayerBonus, true, units, false);
                    if (group != null)
                        MoveGroupToBottom(group, true);
                }

                _bonusTurnDisplayed = true;
                ProcessTimelineUpdates();
            }
            else
            {
                RemoveBonusGroups();
                _bonusTurnDisplayed = false;
                ProcessTimelineUpdates();
            }
        }

        void ProcessTimelineUpdates()
        {
            EnsureFutureProjections();

            var desiredCounts = ComputeDesiredSlotCounts();
            TrimExcessSlots(desiredCounts);
            FillSlots(desiredCounts);

            if (HasAnimationWork())
                StartScrollAnimation();
            else
                FinalizeAnimationState();

            if (turnManager != null)
                HighlightActiveSlot(turnManager.ActiveUnit);
        }

        void PlaceGroupBeforeActive(PhaseGroup group, bool animate)
        {
            if (group == null)
                return;

            group.isProjected = true;
            int targetIndex = GetFirstActiveGroupIndex();
            PlaceGroup(group, targetIndex, animate);
        }

        void MoveGroupToBottom(PhaseGroup group, bool animate)
        {
            if (group == null)
                return;

            group.isProjected = false;
            int targetIndex = _groups.Count;
            PlaceGroup(group, targetIndex, animate);
        }

        int GetFirstActiveGroupIndex()
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                var existing = _groups[i];
                if (existing == null)
                    continue;
                if (!existing.isProjected && !existing.isBonus)
                    return i;
            }

            return _groups.Count;
        }

        void PlaceGroup(PhaseGroup group, int targetIndex, bool animate)
        {
            if (group == null || group.headerEntry == null || _contentRoot == null)
                return;

            group.pendingRemoval = false;

            var entries = CollectGroupEntries(group);

            int currentIndex = _groups.IndexOf(group);
            if (currentIndex >= 0)
            {
                _groups.RemoveAt(currentIndex);
                if (currentIndex < targetIndex)
                    targetIndex--;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                int entryIndex = _entries.IndexOf(entry);
                if (entryIndex >= 0)
                    _entries.RemoveAt(entryIndex);

                if (entry.root != null && entry.root.hierarchy.parent != null)
                    entry.root.RemoveFromHierarchy();
            }

            targetIndex = Mathf.Clamp(targetIndex, 0, _groups.Count);
            int entryInsertIndex = CalculateEntryInsertIndex(targetIndex);

            _groups.Insert(targetIndex, group);

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                entry.isNewlyAdded = animate && i == 0;
                entry.isPendingRemoval = false;
                entry.root.style.opacity = 1f;
                entry.root.style.translate = default;

                _entries.Insert(entryInsertIndex + i, entry);
                _contentRoot.Insert(entryInsertIndex + i, entry.root);
            }
        }

        int CalculateEntryInsertIndex(int groupIndex)
        {
            int index = 0;
            for (int i = 0; i < groupIndex && i < _groups.Count; i++)
            {
                var group = _groups[i];
                index += CountGroupEntries(group);
            }

            return index;
        }

        int CountGroupEntries(PhaseGroup group)
        {
            if (group == null)
                return 0;

            int count = 0;
            if (group.headerEntry != null && !group.headerEntry.isPendingRemoval)
                count++;

            if (group.visibleSlots != null)
            {
                for (int i = 0; i < group.visibleSlots.Count; i++)
                {
                    var entry = group.visibleSlots[i];
                    if (entry == null || entry.isPendingRemoval)
                        continue;
                    count++;
                }
            }

            return count;
        }

        PhaseGroup FindPhaseGroup(int phaseIndex, bool isPlayer)
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];
                if (group == null || group.isBonus)
                    continue;
                if (group.phaseIndex == phaseIndex && group.isPlayer == isPlayer)
                    return group;
            }

            return null;
        }

        PhaseGroup FindBonusGroup(bool? isPlayer = null)
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];
                if (group == null || !group.isBonus)
                    continue;
                if (!isPlayer.HasValue || group.isPlayer == isPlayer.Value)
                    return group;
            }

            return null;
        }

        List<TimelineEntryView> CollectGroupEntries(PhaseGroup group)
        {
            List<TimelineEntryView> list = new();
            if (group == null)
                return list;

            if (group.headerEntry != null)
                list.Add(group.headerEntry);

            if (group.visibleSlots != null)
            {
                for (int i = 0; i < group.visibleSlots.Count; i++)
                {
                    var slot = group.visibleSlots[i];
                    if (slot != null)
                        list.Add(slot);
                }
            }

            return list;
        }

        void RefreshGroupUnits(PhaseGroup group, IReadOnlyList<Unit> units, string label = null, bool projected = false)
        {
            if (group == null)
                return;

            if (!string.IsNullOrEmpty(label) && group.headerEntry != null && group.headerEntry.header.label != null)
                group.headerEntry.header.label.text = label;

            if (!string.IsNullOrEmpty(label))
                group.label = label;

            group.isProjected = projected;
            group.pendingRemoval = false;

            group.pendingUnits = BuildUnitQueue(units);

            if (group.visibleSlots != null)
            {
                for (int i = group.visibleSlots.Count - 1; i >= 0; i--)
                {
                    var entry = group.visibleSlots[i];
                    if (entry == null)
                        continue;
                    RemoveSlotEntry(entry, false);
                }

                group.visibleSlots.Clear();
            }
        }

        Queue<Unit> BuildUnitQueue(IReadOnlyList<Unit> units)
        {
            var queue = new Queue<Unit>();
            if (units == null)
                return queue;

            for (int i = units.Count - 1; i >= 0; i--)
            {
                var unit = units[i];
                if (unit != null)
                    queue.Enqueue(unit);
            }

            return queue;
        }

        void EnsureFutureProjections()
        {
            if (turnManager == null)
                return;

            int targetSlots = Mathf.Max(maxVisibleSlots + 4, 4);
            int guard = 0;
            bool nextIsPlayer = _nextPhaseIsPlayer;
            int knownSlots = CountTotalKnownSlots();

            while (knownSlots < targetSlots && guard++ < 8)
            {
                int nextPhaseIndex = Mathf.Max(1, _phaseCounter + 1);
                var units = turnManager.GetSideUnits(nextIsPlayer);
                if (units == null || units.Count == 0)
                    break;

                var existing = FindPhaseGroup(nextPhaseIndex, nextIsPlayer);
                if (existing != null)
                {
                    RefreshGroupUnits(existing, units, FormatTurnLabel(nextPhaseIndex), true);
                    PlaceGroupBeforeActive(existing, false);
                }
                else
                {
                    var projected = CreatePhaseGroup(nextPhaseIndex, FormatTurnLabel(nextPhaseIndex), nextIsPlayer, false, units, true);
                    if (projected != null)
                        PlaceGroupBeforeActive(projected, false);
                }

                _phaseCounter = Math.Max(_phaseCounter, nextPhaseIndex);
                knownSlots = CountTotalKnownSlots();
                nextIsPlayer = !nextIsPlayer;
            }

            _nextPhaseIsPlayer = nextIsPlayer;
        }

        int CountTotalKnownSlots()
        {
            int total = 0;
            for (int i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];
                total += CountRemainingUnits(group);
            }

            return total;
        }

        List<SlotDemand> ComputeDesiredSlotCounts()
        {
            List<SlotDemand> demands = new();
            int capacity = Mathf.Max(maxVisibleSlots, 1);
            if (capacity <= 0)
                return demands;

            if (_bonusTurnDisplayed)
            {
                var bonus = FindBonusGroup();
                if (bonus != null && !bonus.pendingRemoval)
                {
                    int remaining = CountRemainingUnits(bonus);
                    if (remaining > 0)
                    {
                        int desired = Mathf.Min(remaining, capacity);
                        desired = Mathf.Max(1, desired);
                        demands.Add(new SlotDemand { group = bonus, desiredCount = desired });
                    }
                }

                return demands;
            }

            List<PhaseGroup> ordered = new();
            for (int i = _groups.Count - 1; i >= 0; i--)
            {
                var group = _groups[i];
                if (group == null || group.pendingRemoval)
                    continue;

                int remaining = CountRemainingUnits(group);
                if (remaining <= 0)
                    continue;

                ordered.Add(group);
            }

            if (ordered.Count == 0)
                return demands;

            int[] allocations = new int[ordered.Count];
            int remainingCapacity = capacity;

            for (int i = 0; i < ordered.Count && remainingCapacity > 0; i++)
            {
                allocations[i] = 1;
                remainingCapacity--;
            }

            if (remainingCapacity > 0)
            {
                for (int i = 0; i < ordered.Count && remainingCapacity > 0; i++)
                {
                    int remainingUnits = CountRemainingUnits(ordered[i]);
                    int canAdd = Mathf.Max(0, remainingUnits - allocations[i]);
                    if (canAdd <= 0)
                        continue;

                    int grant = Mathf.Min(canAdd, remainingCapacity);
                    allocations[i] += grant;
                    remainingCapacity -= grant;
                }
            }

            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                int desired = allocations[i];
                if (desired <= 0)
                    continue;

                demands.Add(new SlotDemand
                {
                    group = ordered[i],
                    desiredCount = desired
                });
            }

            return demands;
        }

        void TrimExcessSlots(List<SlotDemand> demands)
        {
            var desiredMap = new Dictionary<PhaseGroup, int>();
            if (demands != null)
            {
                for (int i = 0; i < demands.Count; i++)
                {
                    var demand = demands[i];
                    if (demand.group == null)
                        continue;
                    desiredMap[demand.group] = Mathf.Max(0, demand.desiredCount);
                }
            }

            for (int i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];
                if (group == null)
                    continue;

                int desiredCount = desiredMap.TryGetValue(group, out var count) ? count : 0;
                bool requeue = !group.pendingRemoval;
                ReduceGroupVisibleSlots(group, desiredCount, requeue);
                SetGroupHeaderVisibility(group, desiredCount > 0);
            }
        }

        void ReduceGroupVisibleSlots(PhaseGroup group, int desiredCount, bool requeue)
        {
            if (group == null)
                return;

            int visible = CountVisibleSlots(group);
            if (visible <= desiredCount)
                return;

            while (visible > desiredCount && group.visibleSlots.Count > 0)
            {
                var entry = group.visibleSlots[0];
                if (entry == null)
                {
                    group.visibleSlots.RemoveAt(0);
                    continue;
                }

                if (entry.isPendingRemoval)
                {
                    group.visibleSlots.RemoveAt(0);
                    continue;
                }

                RemoveSlotEntry(entry, requeue);
                visible--;
            }
        }

        void SetGroupHeaderVisibility(PhaseGroup group, bool visible)
        {
            if (group == null || group.headerEntry == null || group.headerEntry.root == null)
                return;

            if (group.headerVisible == visible)
                return;

            group.headerVisible = visible;
            group.headerEntry.root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        int CountVisibleSlots(PhaseGroup group)
        {
            if (group == null || group.visibleSlots == null)
                return 0;

            int count = 0;
            for (int i = 0; i < group.visibleSlots.Count; i++)
            {
                var entry = group.visibleSlots[i];
                if (entry == null || entry.isPendingRemoval)
                    continue;
                count++;
            }

            return count;
        }

        int CountRemainingUnits(PhaseGroup group)
        {
            if (group == null || group.pendingRemoval)
                return 0;

            int pending = group.pendingUnits != null ? group.pendingUnits.Count : 0;
            int visible = CountVisibleSlots(group);
            return pending + visible;
        }

        void RemoveBonusGroups()
        {
            for (int i = _groups.Count - 1; i >= 0; i--)
            {
                var group = _groups[i];
                if (group == null || !group.isBonus)
                    continue;

                if (group.visibleSlots != null)
                {
                    for (int j = group.visibleSlots.Count - 1; j >= 0; j--)
                    {
                        var entry = group.visibleSlots[j];
                        RemoveSlotEntry(entry, false);
                    }

                    group.visibleSlots.Clear();
                }

                MarkGroupForRemoval(group);
            }
        }

        void PrependPendingUnit(PhaseGroup group, Unit unit)
        {
            if (group == null || unit == null)
                return;

            var existing = group.pendingUnits != null ? group.pendingUnits.ToArray() : Array.Empty<Unit>();
            var queue = new Queue<Unit>();
            queue.Enqueue(unit);
            for (int i = 0; i < existing.Length; i++)
            {
                var u = existing[i];
                if (u != null)
                    queue.Enqueue(u);
            }

            group.pendingUnits = queue;
        }

        void RemoveSlotEntry(TimelineEntryView entry, bool requeueUnit)
        {
            if (entry == null)
                return;

            var group = entry.group;

            if (requeueUnit && group != null && entry.unit != null)
                PrependPendingUnit(group, entry.unit);

            entry.isPendingRemoval = true;

            if (group != null)
            {
                group.visibleSlots.Remove(entry);
                if (CountRemainingUnits(group) == 0)
                    MarkGroupForRemoval(group);
            }

            if (_activeSlotEntry == entry)
            {
                ToggleSlotActive(_activeSlotEntry, false);
                _activeSlotEntry = null;
            }
        }

        bool MarkBottomSlotForRemoval()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry.kind != TimelineEntryKind.Slot || entry.isPendingRemoval)
                    continue;

                RemoveSlotEntry(entry, true);
                return true;
            }

            return false;
        }

        PhaseGroup CreatePhaseGroup(int phaseIndex, string label, bool isPlayer, bool isBonus, IReadOnlyList<Unit> units, bool projected)
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

            var group = new PhaseGroup
            {
                label = label,
                isPlayer = isPlayer,
                isBonus = isBonus,
                isProjected = projected,
                phaseIndex = phaseIndex,
                headerEntry = headerEntry,
                pendingUnits = BuildUnitQueue(units),
                visibleSlots = new List<TimelineEntryView>(),
                headerVisible = true
            };

            headerEntry.group = group;
            return group;
        }

        bool FillSlots(bool animate = true)
        {
            var demands = ComputeDesiredSlotCounts();
            return FillSlots(demands, animate);
        }

        bool FillSlots(List<SlotDemand> demands, bool animate = true)
        {
            if (_contentRoot == null || demands == null || demands.Count == 0)
                return false;

            bool added = false;
            bool progress = true;

            while (progress)
            {
                progress = false;

                for (int i = 0; i < demands.Count; i++)
                {
                    var demand = demands[i];
                    var group = demand.group;
                    if (group == null || group.pendingRemoval)
                        continue;

                    int desired = Mathf.Max(0, demand.desiredCount);
                    int visible = CountVisibleSlots(group);
                    if (visible >= desired)
                        continue;

                    while (visible < desired && group.pendingUnits.Count > 0)
                    {
                        var unit = group.pendingUnits.Dequeue();
                        var entry = CreateSlotEntry(unit, group);
                        entry.isNewlyAdded = animate;

                        int insertIndex = GetSlotInsertIndex(group);
                        _entries.Insert(insertIndex, entry);
                        _contentRoot.Insert(insertIndex, entry.root);
                        group.visibleSlots.Add(entry);
                        visible++;
                        added = true;
                        progress = true;
                    }
                }
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

            RemoveSlotEntry(entry, false);
        }

        void MarkGroupForRemoval(PhaseGroup group)
        {
            if (group == null || group.pendingRemoval)
                return;

            group.pendingRemoval = true;
            group.pendingUnits?.Clear();
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

        struct SlotDemand
        {
            public PhaseGroup group;
            public int desiredCount;
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
            public bool isProjected;
            public int phaseIndex = -1;
            public TimelineEntryView headerEntry;
            public Queue<Unit> pendingUnits = new();
            public List<TimelineEntryView> visibleSlots = new();
            public bool pendingRemoval;
            public bool headerVisible = true;
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
