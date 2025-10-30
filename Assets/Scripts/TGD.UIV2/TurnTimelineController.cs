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
        [Header("Runtime")]
        public TurnManagerV2 turnManager;
        public CombatActionManagerV2 combatManager;
        public UIDocument document;

        [Header("Look")]
        public Sprite fallbackAvatar;
        [Min(1)] public int maxVisibleSlots = 4;

        readonly HashSet<Unit> _completedThisPhase = new();

        VisualElement _contentRoot;
        Unit _activeUnit;
        bool _activePhaseIsPlayer = true;
        int _activePhaseIndex = 1;

        enum EntryKind
        {
            Header,
            Slot
        }

        struct DisplayEntry
        {
            public EntryKind kind;
            public string label;
            public Unit unit;
            public bool isPlayer;
            public bool isActive;
        }

        static T AutoFind<T>() where T : Object
        {
    #if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
    #else
            return Object.FindObjectOfType<T>();
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
            SyncPhaseState();
            RebuildTimeline();
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
            _contentRoot?.Clear();
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

        void ClearAll()
        {
            _completedThisPhase.Clear();
            _activeUnit = null;

            if (_contentRoot != null)
                _contentRoot.Clear();
        }

        void SyncPhaseState()
        {
            if (turnManager == null)
            {
                _activePhaseIsPlayer = true;
                _activePhaseIndex = 1;
                _activeUnit = null;
                _completedThisPhase.Clear();
                return;
            }

            _activePhaseIsPlayer = turnManager.IsPlayerPhase;
            _activePhaseIndex = Mathf.Max(1, turnManager.CurrentPhaseIndex);
            _activeUnit = turnManager.ActiveUnit;
            _completedThisPhase.Clear();
        }

        void OnPhaseBegan(bool isPlayerPhase)
        {
            _activePhaseIsPlayer = isPlayerPhase;
            _activePhaseIndex = turnManager != null ? Mathf.Max(1, turnManager.CurrentPhaseIndex) : 1;
            _completedThisPhase.Clear();
            _activeUnit = null;
            RebuildTimeline();
        }

        void OnTurnStarted(Unit unit)
        {
            if (unit == null)
                return;

            _activeUnit = unit;
            RebuildTimeline();
        }

        void OnTurnEnded(Unit unit)
        {
            if (unit == null)
                return;

            _completedThisPhase.Add(unit);
            if (_activeUnit == unit)
                _activeUnit = null;

            RebuildTimeline();
        }

        void OnBonusTurnStateChanged()
        {
            RebuildTimeline();
        }

        void RebuildTimeline()
        {
            if (_contentRoot == null)
                return;

            List<DisplayEntry> entries = combatManager != null && combatManager.IsBonusTurnActive
                ? BuildBonusEntries()
                : BuildPhaseEntries();

            _contentRoot.Clear();

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.kind == EntryKind.Header)
                {
                    var header = CreateHeaderVisuals();
                    if (header.root != null)
                    {
                        header.label.text = entry.label;
                        _contentRoot.Add(header.root);
                    }
                }
                else
                {
                    var slot = CreateSlotVisuals();
                    if (slot.root != null)
                    {
                        ApplySlotVisuals(slot, entry);
                        _contentRoot.Add(slot.root);
                    }
                }
            }
        }

        List<DisplayEntry> BuildBonusEntries()
        {
            List<DisplayEntry> entries = new();
            if (combatManager == null || turnManager == null)
                return entries;

            var bonusUnit = combatManager.CurrentBonusTurnUnit;
            bool isPlayerBonus = turnManager.IsPlayerUnit(bonusUnit);
            var units = turnManager.GetSideUnits(isPlayerBonus);
            if (units == null || units.Count == 0)
                return entries;

            List<Unit> ordered = BuildDisplayOrder(units);
            int maxUnits = Mathf.Max(maxVisibleSlots, 1);
            if (ordered.Count > maxUnits)
                ordered = ordered.GetRange(ordered.Count - maxUnits, maxUnits);

            entries.Add(new DisplayEntry
            {
                kind = EntryKind.Header,
                label = "Bonus Turn",
                isPlayer = isPlayerBonus
            });

            for (int i = 0; i < ordered.Count; i++)
            {
                var unit = ordered[i];
                entries.Add(new DisplayEntry
                {
                    kind = EntryKind.Slot,
                    unit = unit,
                    isPlayer = isPlayerBonus,
                    isActive = unit == bonusUnit
                });
            }

            return entries;
        }

        List<DisplayEntry> BuildPhaseEntries()
        {
            List<DisplayEntry> entries = new();
            if (turnManager == null)
                return entries;

            // Refresh phase state from manager in case we missed a callback during initialization.
            _activePhaseIsPlayer = turnManager.IsPlayerPhase;
            _activePhaseIndex = Mathf.Max(1, turnManager.CurrentPhaseIndex);
            if (_activeUnit == null)
                _activeUnit = turnManager.ActiveUnit;

            Unit activeHighlight;
            List<Unit> activeUnits = BuildActivePhaseUnits(out activeHighlight);
            int visibleCapacity = Mathf.Max(maxVisibleSlots, 1);
            List<Unit> activeDisplay = TrimToTail(activeUnits, visibleCapacity);
            int totalCapacity = visibleCapacity + 1;
            int previewCapacity = Mathf.Max(0, totalCapacity - activeDisplay.Count);

            List<DisplayEntry> futureEntries = BuildFuturePhaseEntries(previewCapacity);
            entries.AddRange(futureEntries);

            if (activeDisplay.Count > 0)
            {
                entries.Add(new DisplayEntry
                {
                    kind = EntryKind.Header,
                    label = FormatTurnLabel(_activePhaseIndex),
                    isPlayer = _activePhaseIsPlayer
                });

                for (int i = 0; i < activeDisplay.Count; i++)
                {
                    var unit = activeDisplay[i];
                    entries.Add(new DisplayEntry
                    {
                        kind = EntryKind.Slot,
                        unit = unit,
                        isPlayer = _activePhaseIsPlayer,
                        isActive = unit == activeHighlight
                    });
                }
            }
            else
            {
                entries.Add(new DisplayEntry
                {
                    kind = EntryKind.Header,
                    label = FormatTurnLabel(_activePhaseIndex),
                    isPlayer = _activePhaseIsPlayer
                });
            }

            return entries;
        }

        List<DisplayEntry> BuildFuturePhaseEntries(int previewCapacity)
        {
            List<DisplayEntry> entries = new();
            if (previewCapacity <= 0 || turnManager == null)
                return entries;

            List<(int index, bool isPlayer, List<Unit> units)> buffer = new();
            bool nextIsPlayer = !_activePhaseIsPlayer;
            int nextIndex = _activePhaseIndex + 1;
            int guard = 0;

            while (previewCapacity > 0 && guard++ < 8)
            {
                var units = BuildDisplayOrder(turnManager.GetSideUnits(nextIsPlayer));
                if (units.Count > 0)
                {
                    int take = Mathf.Min(units.Count, previewCapacity);
                    var slice = TrimToTail(units, take);
                    buffer.Add((nextIndex, nextIsPlayer, slice));
                    previewCapacity -= slice.Count;
                }

                nextIsPlayer = !nextIsPlayer;
                nextIndex++;
            }

            for (int i = buffer.Count - 1; i >= 0; i--)
            {
                var phase = buffer[i];
                if (phase.units == null || phase.units.Count == 0)
                    continue;

                entries.Add(new DisplayEntry
                {
                    kind = EntryKind.Header,
                    label = FormatTurnLabel(phase.index),
                    isPlayer = phase.isPlayer
                });

                for (int j = 0; j < phase.units.Count; j++)
                {
                    entries.Add(new DisplayEntry
                    {
                        kind = EntryKind.Slot,
                        unit = phase.units[j],
                        isPlayer = phase.isPlayer,
                        isActive = false
                    });
                }
            }

            return entries;
        }

        List<Unit> BuildActivePhaseUnits(out Unit activeHighlight)
        {
            activeHighlight = null;
            var units = turnManager != null ? turnManager.GetSideUnits(_activePhaseIsPlayer) : null;
            if (units == null || units.Count == 0)
                return new List<Unit>();

            List<Unit> filtered = new();
            Unit pendingActive = _activeUnit;

            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null)
                    continue;

                if (_completedThisPhase.Contains(unit))
                    continue;

                filtered.Add(unit);
                if (pendingActive == null)
                    pendingActive = unit;
            }

            if (pendingActive != null && !filtered.Contains(pendingActive))
                filtered.Add(pendingActive);

            filtered.Reverse();
            activeHighlight = pendingActive != null && filtered.Contains(pendingActive) ? pendingActive : null;
            return filtered;
        }

        static List<Unit> BuildDisplayOrder(IReadOnlyList<Unit> units)
        {
            List<Unit> ordered = new();
            if (units == null)
                return ordered;

            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit != null)
                    ordered.Add(unit);
            }

            ordered.Reverse();
            return ordered;
        }

        static List<Unit> TrimToTail(List<Unit> units, int count)
        {
            if (units == null || count <= 0)
                return new List<Unit>();

            if (units.Count <= count)
                return new List<Unit>(units);

            return units.GetRange(units.Count - count, count);
        }

        void ApplySlotVisuals(SlotVisuals visuals, DisplayEntry entry)
        {
            if (visuals.card != null)
            {
                visuals.card.RemoveFromClassList("player-turn");
                visuals.card.RemoveFromClassList("enemy-turn");
                visuals.card.RemoveFromClassList("slot-active");
                visuals.card.AddToClassList(entry.isPlayer ? "player-turn" : "enemy-turn");
                if (entry.isActive)
                    visuals.card.AddToClassList("slot-active");
            }

            if (visuals.icon != null)
            {
                var sprite = ResolveAvatar(entry.unit);
                if (sprite != null)
                    visuals.icon.style.backgroundImage = new StyleBackground(sprite);
                else
                    visuals.icon.style.backgroundImage = StyleKeyword.Null;
            }

            if (visuals.label != null)
                visuals.label.style.display = DisplayStyle.None;
        }

        Sprite ResolveAvatar(Unit unit)
        {
            if (unit != null && UnitAvatarRegistry.TryGetAvatar(unit, out var sprite) && sprite != null)
                return sprite;
            return fallbackAvatar;
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
            label.style.display = DisplayStyle.None;
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
