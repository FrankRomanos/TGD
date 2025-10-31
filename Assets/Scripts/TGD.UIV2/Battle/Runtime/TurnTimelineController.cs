using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using TGD.CombatV2;
using TGD.HexBoard;

namespace TGD.UIV2.Battle
{
    /// <summary>
    /// Drives the UI Toolkit based turn timeline HUD and animates the live queue of turn groups.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TurnTimelineController : MonoBehaviour
    {
        [Header("Runtime")]
        private TurnManagerV2 turnManager;
        private CombatActionManagerV2 combatManager;
        public UIDocument document;

        [Header("Look")]
        public Sprite fallbackAvatar;
        [Min(1)] public int maxVisibleSlots = 4;

        readonly HashSet<Unit> _completedThisPhase = new();
        readonly List<SlotEntryVisual> _slotEntries = new();

        public event System.Action<TGD.HexBoard.Unit> ActiveUnitDeferred;

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
        Coroutine _pendingFullRoundRefresh;
        bool _isInitialized;

        enum EntryKind
        {
            Header,
            Slot,
            Event
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
            public string auxText;
        }

        void Awake()
        {
            if (!document)
                document = GetComponent<UIDocument>();
            InitializeRoot();
        }

        void OnEnable()
        {
            // BattleUIService is now responsible for initializing and driving the timeline.
        }

        void OnDisable()
        {
            Shutdown();
        }

        public void Initialize(
            TurnManagerV2 turnManager,
            CombatActionManagerV2 combatManager
        )
        {
            this.turnManager = turnManager;
            this.combatManager = combatManager;

            if (this.turnManager == null || this.combatManager == null)
            {
                _isInitialized = false;
                ClearAll();
                return;
            }

            InitializeRoot();     // 确保 _contentRoot / _dragOverlay
            SyncPhaseState();     // 根据 turnManager 当前状态刷新 _activePhaseIsPlayer / _activeUnit 等
            _isInitialized = true;
            RebuildTimeline();    // 立刻先画一版（可能还是空队伍）
        }
        public void RefreshNow()
        {
            if (!_isInitialized)
                return;

            SyncPhaseState();   // 你已经有这个方法
            RebuildTimeline();  // 你已经有这个方法
        }
        public void Shutdown()
        {
            CancelPendingFullRoundRefresh();
            ClearAll();
            _isInitialized = false;
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

        public void NotifyPhaseBeganExternal(bool isPlayerPhase)
        {
            if (!_isInitialized || !isActiveAndEnabled)
                return;

            _activePhaseIsPlayer = isPlayerPhase;
            _activePhaseIndex = turnManager != null ? Mathf.Max(1, turnManager.CurrentPhaseIndex) : 1;
            _completedThisPhase.Clear();
            _activeUnit = null;
            RebuildTimeline();
        }

        public void NotifyTurnStartedExternal(Unit unit)
        {
            if (!_isInitialized || !isActiveAndEnabled)
                return;

            if (unit == null)
                return;

            _activeUnit = unit;
            RebuildTimeline();
            CancelPendingFullRoundRefresh();
            if (turnManager != null && turnManager.HasActiveFullRound(unit))
                _pendingFullRoundRefresh = StartCoroutine(RefreshTimelineAfterFullRound(unit));
        }

        public void NotifyTurnEndedExternal(Unit unit)
        {
            if (!_isInitialized || !isActiveAndEnabled)
                return;

            if (unit == null)
                return;

            _completedThisPhase.Add(unit);
            if (_activeUnit == unit)
                _activeUnit = null;

            RebuildTimeline();
        }

        public void NotifyBonusTurnStateChangedExternal()
        {
            if (!_isInitialized || !isActiveAndEnabled)
                return;

            RebuildTimeline();
        }

        public void NotifyTurnOrderChangedExternal(bool isPlayerSide)
        {
            if (!_isInitialized || !isActiveAndEnabled)
                return;

            if (isPlayerSide)
                RebuildTimeline();
        }

        public void ForceRebuildNow()
        {
            RebuildTimeline();
        }

        void RebuildTimeline()
        {
            if (!_isInitialized || !isActiveAndEnabled)
                return;

            if (_contentRoot == null)
            {
                InitializeRoot();
                if (_contentRoot == null)
                    return;
            }

            ClearDragState();
            _slotEntries.Clear();

            List<DisplayEntry> entries = combatManager != null && combatManager.IsBonusTurnActive
                ? BuildBonusEntries()
                : BuildPhaseEntries();

            _contentRoot.Clear();

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                switch (entry.kind)
                {
                    case EntryKind.Header:
                    {
                        var header = CreateHeaderVisuals();
                        if (header.root != null)
                        {
                            header.label.text = entry.label;
                            _contentRoot.Add(header.root);
                        }
                        break;
                    }
                    case EntryKind.Slot:
                    {
                        var slot = CreateSlotVisuals();
                        if (slot.root != null)
                        {
                            ApplySlotVisuals(slot, entry);
                            _contentRoot.Add(slot.root);
                            RegisterSlotInteractions(slot, entry);
                        }
                        break;
                    }
                    case EntryKind.Event:
                    {
                        var visuals = CreateEventVisuals();
                        if (visuals.root != null)
                        {
                            ApplyEventVisuals(visuals, entry);
                            _contentRoot.Add(visuals.root);
                        }
                        break;
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

        void CancelPendingFullRoundRefresh()
        {
            if (_pendingFullRoundRefresh != null)
            {
                StopCoroutine(_pendingFullRoundRefresh);
                _pendingFullRoundRefresh = null;
            }
        }

        IEnumerator RefreshTimelineAfterFullRound(Unit unit)
        {
            yield return null;
            _pendingFullRoundRefresh = null;
            if (this == null)
                yield break;
            if (!isActiveAndEnabled)
                yield break;
            if (unit == null)
                yield break;
            RebuildTimeline();
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
            TGD.HexBoard.Unit deferredUnit = null;

            if (applyDrop && _activeDrag != null && _currentDropTarget != null && turnManager != null)
            {
                deferredUnit = _activeDrag.entry.unit;

                if (turnManager.TryDeferActivePlayerUnit(_currentDropTarget.entry.unit))
                {
                    applied = true;
                }
            }

            ClearDragState();

            if (applied)
            {
                RebuildTimeline();
                ActiveUnitDeferred?.Invoke(deferredUnit);
            }
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
            // 1) 基础合法性检查
            if (_activeDrag == null || slot == null)
                return false;

            // 只能在玩家自己当回合时拖自己，不能拿敌人拖敌人，不能拿非当前单位拖
            if (!slot.entry.isPlayer || !slot.entry.isActivePhase)
                return false;

            if (slot.entry.unit == null || slot.entry.unit == _activeDrag.entry.unit)
                return false;

            if (turnManager == null)
                return false;

            // 2) 安全地拿 candidateIndex
            int candidateIndex = slot.entry.turnOrderIndex >= 0
                ? slot.entry.turnOrderIndex
                : SafeGetTurnOrderIndex(slot.entry.unit, slot.entry.isPlayer);

            if (candidateIndex < 0)
                return false; // 说明这个slot现在不在turn order里，拖它没有意义

            // 3) 我们自己的 index（_activeDrag 的顺位）
            if (_activeDragOrderIndex == int.MaxValue)
            {
                _activeDragOrderIndex = _activeDrag.entry.turnOrderIndex >= 0
                    ? _activeDrag.entry.turnOrderIndex
                    : SafeGetTurnOrderIndex(_activeDrag.entry.unit, _activeDrag.entry.isPlayer);
            }

            if (_activeDragOrderIndex < 0)
                return false; // 我自己都不在队列里？那这次拖拽直接视为无效

            // 4) 最后才比较谁在后面
            // （注意：你想的是“把我塞到他后面”，所以要允许 candidateIndex >= myIndex+1）
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
                    var slotEntry = new DisplayEntry
                    {
                        kind = EntryKind.Slot,
                        unit = unit,
                        isPlayer = isPlayerBonus,
                        isActive = unit == bonusUnit,
                        isActivePhase = false,
                        turnOrderIndex = turnManager != null ? turnManager.GetTurnOrderIndex(unit, isPlayerBonus) : -1
                    };
                    entries.Add(slotEntry);
                    AppendEventEntryIfNeeded(entries, slotEntry);
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
                    var slotEntry = new DisplayEntry
                    {
                        kind = EntryKind.Slot,
                        unit = unit,
                        isPlayer = _activePhaseIsPlayer,
                        isActive = unit == activeHighlight,
                        isActivePhase = true,
                        turnOrderIndex = turnManager != null ? turnManager.GetTurnOrderIndex(unit, _activePhaseIsPlayer) : -1
                    };
                    entries.Add(slotEntry);
                    AppendEventEntryIfNeeded(entries, slotEntry);
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
                    var slotEntry = new DisplayEntry
                    {
                        kind = EntryKind.Slot,
                        unit = phase.units[j],
                        isPlayer = phase.isPlayer,
                        isActive = false,
                        isActivePhase = false,
                        turnOrderIndex = -1
                    };
                    entries.Add(slotEntry);
                    AppendEventEntryIfNeeded(entries, slotEntry);
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

        void ApplyEventVisuals(EventVisuals visuals, DisplayEntry entry)
        {
            if (visuals.card != null)
            {
                visuals.card.RemoveFromClassList("player-turn");
                visuals.card.RemoveFromClassList("enemy-turn");
                visuals.card.AddToClassList(entry.isPlayer ? "player-turn" : "enemy-turn");
            }

            if (visuals.icon != null)
            {
                var sprite = ResolveAvatar(entry.unit);
                if (sprite != null)
                    visuals.icon.style.backgroundImage = new StyleBackground(sprite);
                else
                    visuals.icon.style.backgroundImage = StyleKeyword.Null;
            }

            if (visuals.text != null)
                visuals.text.text = entry.auxText ?? string.Empty;
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

        EventVisuals CreateEventVisuals()
        {
            var root = new VisualElement();
            root.AddToClassList("event-root");

            var row = new VisualElement();
            row.AddToClassList("event-row");
            root.Add(row);

            var card = new VisualElement();
            card.AddToClassList("event-card");
            row.Add(card);

            var icon = new VisualElement();
            icon.AddToClassList("event-icon");
            card.Add(icon);

            var text = new Label();
            text.AddToClassList("event-text");
            row.Add(text);

            return new EventVisuals
            {
                root = root,
                row = row,
                card = card,
                icon = icon,
                text = text
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
        struct EventVisuals
        {
            public VisualElement root;
            public VisualElement row;
            public VisualElement card;
            public VisualElement icon;
            public Label text;
        }

        void AppendEventEntryIfNeeded(List<DisplayEntry> entries, DisplayEntry slotEntry)
        {
            if (slotEntry.unit == null)
                return;

            string eventText = GetPendingFullRoundText(slotEntry.unit);
            if (string.IsNullOrEmpty(eventText))
                return;

            entries.Add(new DisplayEntry
            {
                kind = EntryKind.Event,
                unit = slotEntry.unit,
                isPlayer = slotEntry.isPlayer,
                isActive = false,
                isActivePhase = slotEntry.isActivePhase,
                turnOrderIndex = -1,
                auxText = eventText
            });
        }

        string GetPendingFullRoundText(Unit unit)
        {
            if (unit == null || turnManager == null)
                return string.Empty;

            if (turnManager.TryGetFullRoundInfo(unit, out int roundsRemaining, out int totalRounds, out _))
            {
                int rounds = Mathf.Max(0, roundsRemaining);
                if (rounds > 0)
                    return $"FullRound:{rounds}";

                if (totalRounds > 0)
                    return $"FullRound:{totalRounds}";

                return "FullRound";
            }

            return string.Empty;
        }
        int SafeGetTurnOrderIndex(Unit unit, bool isPlayerSide)
        {
            if (unit == null || turnManager == null)
                return -1;

            try
            {
                // 这一行就是原本会抛异常的调用
                return turnManager.GetTurnOrderIndex(unit, isPlayerSide);
            }
            catch
            {
                // 找不到 / 状态不合法 / 正在切队列
                return -1;
            }
        }
    }
}
