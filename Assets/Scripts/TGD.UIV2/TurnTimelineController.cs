using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using TGD.AudioV2;
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
        readonly List<SlotEntryVisual> _slotEntries = new();

        VisualElement _contentRoot;
        VisualElement _dragOverlay;
        VisualElement _dragGhost;
        Unit _activeUnit;
        bool _activePhaseIsPlayer = true;
        int _activePhaseIndex = 1;
        SlotEntryVisual _activeDrag;
        SlotEntryVisual _currentDropTarget;
        int _activeDragPointerId = PointerId.invalidPointerId;
        Vector2 _dragStartPosition;
        int _activeDragOrderIndex = int.MaxValue;

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
            public bool isActivePhase;
            public int turnOrderIndex;
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

            _dragOverlay = root.Q<VisualElement>("DragOverlay");
            if (_dragOverlay == null)
            {
                _dragOverlay = new VisualElement
                {
                    name = "DragOverlay"
                };
                _dragOverlay.AddToClassList("drag-overlay");
                root.Add(_dragOverlay);
            }
            else
            {
                _dragOverlay.Clear();
            }
            // ✅ 关键补丁：让 overlay 覆盖整张UI并且不吃鼠标
            _dragOverlay.style.position = Position.Absolute;
            _dragOverlay.style.left = 0;
            _dragOverlay.style.top = 0;
            _dragOverlay.style.right = 0;
            _dragOverlay.style.bottom = 0;

            // UI Toolkit里，这个等价“pointer-events:none”
            _dragOverlay.pickingMode = PickingMode.Ignore;
            _dragOverlay?.BringToFront();
        }

        void Subscribe()
        {
            if (turnManager != null)
            {
                turnManager.PhaseBegan += OnPhaseBegan;
                turnManager.TurnStarted += OnTurnStarted;
                turnManager.TurnEnded += OnTurnEnded;
                turnManager.TurnOrderChanged += OnTurnOrderChanged;
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
                turnManager.TurnOrderChanged -= OnTurnOrderChanged;
            }

            if (combatManager != null)
                combatManager.BonusTurnStateChanged -= OnBonusTurnStateChanged;
        }

        void ClearAll()
        {
            ClearDragState();
            _slotEntries.Clear();
            _completedThisPhase.Clear();
            _activeUnit = null;

            if (_contentRoot != null)
                _contentRoot.Clear();

            if (_dragOverlay != null)
                _dragOverlay.Clear();
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

        void OnTurnOrderChanged(bool isPlayerSide)
        {
            if (isPlayerSide)
                RebuildTimeline();
        }

        void RebuildTimeline()
        {
            if (_contentRoot == null)
                return;

            ClearDragState();
            _slotEntries.Clear();

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
                        RegisterSlotInteractions(slot, entry);
                    }
                }
            }
        }

        void ClearDragState()
        {
            if (_activeDragPointerId != PointerId.invalidPointerId &&
                _activeDrag?.visuals.root != null &&
                _activeDrag.visuals.root.HasPointerCapture(_activeDragPointerId))
            {
                _activeDrag.visuals.root.ReleasePointer(_activeDragPointerId);
            }

            // 把原slot恢复
            if (_activeDrag?.visuals.row != null)
                _activeDrag.visuals.row.style.translate = StyleKeyword.Null;

            if (_activeDrag?.visuals.card != null)
            {
                _activeDrag.visuals.card.RemoveFromClassList("slot-drag-origin");
                _activeDrag.visuals.card.style.opacity = StyleKeyword.Null; // 恢复显示
            }

            if (_activeDrag?.visuals.root != null)
                _activeDrag.visuals.root.RemoveFromClassList("slot-dragging");

            // 清掉上一次高亮的插入目标
            if (_currentDropTarget?.visuals.card != null)
                _currentDropTarget.visuals.card.RemoveFromClassList("slot-drop-target");

            if (_currentDropTarget?.visuals.insertMarker != null)
                _currentDropTarget.visuals.insertMarker.style.display = DisplayStyle.None;

            // 清掉ghost
            if (_dragGhost != null)
            {
                _dragGhost.RemoveFromHierarchy();
                _dragGhost = null;
            }

            _activeDrag = null;
            _currentDropTarget = null;
            _activeDragPointerId = PointerId.invalidPointerId;
            _dragStartPosition = default;
            _activeDragOrderIndex = int.MaxValue;
        }

        void RegisterSlotInteractions(SlotVisuals visuals, DisplayEntry entry)
        {
            var slot = new SlotEntryVisual
            {
                entry = entry,
                visuals = visuals
            };

            _slotEntries.Add(slot);

            if (visuals.root == null)
                return;

            visuals.root.RegisterCallback<PointerDownEvent>(evt => OnSlotPointerDown(evt, slot));
            visuals.root.RegisterCallback<PointerMoveEvent>(evt => OnSlotPointerMove(evt, slot));
            visuals.root.RegisterCallback<PointerUpEvent>(evt => OnSlotPointerUp(evt, slot));
            visuals.root.RegisterCallback<PointerLeaveEvent>(evt => OnSlotPointerLeave(evt, slot));
            visuals.root.RegisterCallback<PointerCancelEvent>(evt => OnSlotPointerCancel(evt, slot));
        }

        void OnSlotPointerDown(PointerDownEvent evt, SlotEntryVisual slot)
        {
            if (evt.button != 0)
                return;

            if (!CanStartDrag(slot))
                return;

            evt.StopPropagation();
            BeginSlotDrag(evt, slot);
        }

        void OnSlotPointerMove(PointerMoveEvent evt, SlotEntryVisual slot)
        {
            if (_activeDrag == null || evt.pointerId != _activeDragPointerId)
                return;

            evt.StopPropagation();
            if (_dragGhost != null)
            {
                _dragGhost.style.left = evt.position.x;
                _dragGhost.style.top = evt.position.y;
            }

            UpdateDropTarget(evt.position);
        }

        void OnSlotPointerUp(PointerUpEvent evt, SlotEntryVisual slot)
        {
            if (_activeDrag == null || evt.pointerId != _activeDragPointerId)
                return;

            evt.StopPropagation();
            FinishSlotDrag(applyDrop: true);
        }

        void OnSlotPointerLeave(PointerLeaveEvent evt, SlotEntryVisual slot)
        {
            if (_activeDrag == null || evt.pointerId != _activeDragPointerId)
                return;

            UpdateDropTarget(evt.position);
        }

        void OnSlotPointerCancel(PointerCancelEvent evt, SlotEntryVisual slot)
        {
            if (_activeDrag == null || evt.pointerId != _activeDragPointerId)
                return;

            evt.StopPropagation();
            FinishSlotDrag(applyDrop: false);
        }

        bool CanStartDrag(SlotEntryVisual slot)
        {
            if (slot == null || turnManager == null)
                return false;

            if (slot.entry.unit == null)
                return false;

            if (!slot.entry.isPlayer || !slot.entry.isActivePhase)
                return false;

            if (_activeUnit == null || slot.entry.unit != _activeUnit)
                return false;

            if (combatManager != null && combatManager.IsBonusTurnActive)
                return false;

            return turnManager.CanDeferActiveUnit(slot.entry.unit);
        }

        void BeginSlotDrag(PointerDownEvent evt, SlotEntryVisual slot)
        {
            ClearDragState();

            _activeDrag = slot;
            _activeDragPointerId = evt.pointerId;
            _dragStartPosition = evt.position;
            _activeDragOrderIndex = slot.entry.turnOrderIndex >= 0 ? slot.entry.turnOrderIndex : turnManager.GetTurnOrderIndex(slot.entry.unit, slot.entry.isPlayer);

            if (_activeDrag.visuals.root != null)
            {
                _activeDrag.visuals.root.BringToFront();
                _activeDrag.visuals.root.CapturePointer(evt.pointerId);
                _activeDrag.visuals.root.AddToClassList("slot-dragging");
            }

            if (_activeDrag.visuals.card != null)
            {
                _activeDrag.visuals.card.AddToClassList("slot-drag-origin");
                _activeDrag.visuals.card.style.opacity = 0f; // 完全透明
            }

            CreateDragGhost();

            UpdateDropTarget(evt.position);
        }

        void FinishSlotDrag(bool applyDrop)
        {
            bool applied = false;

            if (applyDrop && _activeDrag != null && _currentDropTarget != null && turnManager != null)
            {
                if (turnManager.TryDeferActivePlayerUnit(_currentDropTarget.entry.unit))
                {
                    applied = true;
                    BattleAudioManager.PlayEvent(BattleAudioEvent.TurnTimelineInsert);
                }
            }

            ClearDragState();

            if (applied)
                RebuildTimeline();
        }

        void UpdateDropTarget(Vector2 pointerPosition)
        {
            if (_contentRoot == null)
                return;

            var bounds = _contentRoot.worldBound;
            if (!bounds.Contains(pointerPosition))
            {
                ShowDropTarget(null);
                return;
            }

            SlotEntryVisual candidate = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < _slotEntries.Count; i++)
            {
                var slot = _slotEntries[i];
                if (!IsValidDropTarget(slot))
                    continue;

                var root = slot.visuals.root;
                if (root == null)
                    continue;

                Rect world = root.worldBound;
                float distance = Mathf.Abs(pointerPosition.y - world.center.y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    candidate = slot;
                }
            }

            ShowDropTarget(candidate);
        }

        bool IsValidDropTarget(SlotEntryVisual slot)
        {
            if (_activeDrag == null || slot == null)
                return false;

            if (!slot.entry.isPlayer || !slot.entry.isActivePhase)
                return false;

            if (slot.entry.unit == null || slot.entry.unit == _activeDrag.entry.unit)
                return false;

            if (turnManager == null)
                return false;

            int candidateIndex = slot.entry.turnOrderIndex >= 0 ? slot.entry.turnOrderIndex : turnManager.GetTurnOrderIndex(slot.entry.unit, slot.entry.isPlayer);
            if (_activeDragOrderIndex == int.MaxValue)
                _activeDragOrderIndex = turnManager.GetTurnOrderIndex(_activeDrag.entry.unit, _activeDrag.entry.isPlayer);

            return candidateIndex > _activeDragOrderIndex;
        }

        void ShowDropTarget(SlotEntryVisual slot)
        {
            if (ReferenceEquals(_currentDropTarget, slot))
                return;

            if (_currentDropTarget?.visuals.card != null)
                _currentDropTarget.visuals.card.RemoveFromClassList("slot-drop-target");

            if (_currentDropTarget?.visuals.insertMarker != null)
                _currentDropTarget.visuals.insertMarker.style.display = DisplayStyle.None;

            _currentDropTarget = slot;

            if (_currentDropTarget?.visuals.card != null)
                _currentDropTarget.visuals.card.AddToClassList("slot-drop-target");

            if (_currentDropTarget?.visuals.insertMarker != null)
                _currentDropTarget.visuals.insertMarker.style.display = DisplayStyle.None;
        }

        void CreateDragGhost()
        {
            if (_dragOverlay == null || _activeDrag?.visuals.card == null)
                return;

            var card = _activeDrag.visuals.card;
            var icon = _activeDrag.visuals.icon;

            _dragGhost = new VisualElement();
            _dragGhost.AddToClassList("drag-ghost");
            _dragGhost.style.position = Position.Absolute;
            _dragGhost.style.left = _dragStartPosition.x;
            _dragGhost.style.top = _dragStartPosition.y;

            var ghostCard = new VisualElement();
            ghostCard.AddToClassList("slot-card");

            float width = card.worldBound.width;
            if (width <= 0f)
                width = card.resolvedStyle.width;
            float height = card.worldBound.height;
            if (height <= 0f)
                height = card.resolvedStyle.height;

            if (width > 0f)
                ghostCard.style.width = width;
            if (height > 0f)
                ghostCard.style.height = height;

            if (_activeDrag.entry.isPlayer)
                ghostCard.AddToClassList("player-turn");
            else
                ghostCard.AddToClassList("enemy-turn");

            if (_activeDrag.entry.isActive)
                ghostCard.AddToClassList("slot-active");

            var ghostSkin = new VisualElement();
            ghostSkin.AddToClassList("slot-skin");
            ghostCard.Add(ghostSkin);

            var ghostIcon = new VisualElement();
            ghostIcon.AddToClassList("slot-icon");
            if (icon != null)
                ghostIcon.style.backgroundImage = icon.style.backgroundImage;
            ghostSkin.Add(ghostIcon);

            var ghostFrame = new VisualElement();
            ghostFrame.AddToClassList("slot-ornate-frame");
            ghostCard.Add(ghostFrame);

            _dragGhost.Add(ghostCard);
            _dragOverlay.Add(_dragGhost);
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
                    isActive = unit == bonusUnit,
                    isActivePhase = false,
                    turnOrderIndex = turnManager != null ? turnManager.GetTurnOrderIndex(unit, isPlayerBonus) : -1
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
                        isActive = unit == activeHighlight,
                        isActivePhase = true,
                        turnOrderIndex = turnManager != null ? turnManager.GetTurnOrderIndex(unit, _activePhaseIsPlayer) : -1
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
                        isActive = false,
                        isActivePhase = false,
                        turnOrderIndex = -1
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

            if (visuals.insertMarker != null)
                visuals.insertMarker.style.display = DisplayStyle.None;

            if (visuals.row != null)
                visuals.row.style.translate = StyleKeyword.Null;
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
                row = row,
                dragHandle = dragHandle,
                card = card,
                icon = icon,
                label = label,
                insertMarker = insertMarker
            };
        }

        struct HeaderVisuals
        {
            public VisualElement root;
            public Label label;
        }

        sealed class SlotEntryVisual
        {
            public DisplayEntry entry;
            public SlotVisuals visuals;
        }

        struct SlotVisuals
        {
            public VisualElement root;
            public VisualElement row;
            public VisualElement dragHandle;
            public VisualElement card;
            public VisualElement icon;
            public Label label;
            public VisualElement insertMarker;
        }
    }
}
