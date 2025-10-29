using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TGD.CombatV2;
using TGD.HexBoard;

namespace TGD.UIV2
{
    /// <summary>
    /// Drives the prototype turn timeline HUD that pairs TurnManagerV2 phases with slot portraits.
    /// </summary>
    public sealed class TurnTimelineController : MonoBehaviour
    {
        [Header("Runtime Refs")]
        public TurnManagerV2 turnManager;

        [Header("Turn Messages")]
        public TurnTimelineTurnWidget[] turnWidgets = Array.Empty<TurnTimelineTurnWidget>();

        [Header("Unit Slots")]
        public TurnTimelineSlotWidget[] slotWidgets = Array.Empty<TurnTimelineSlotWidget>();

        [Header("Look")]
        public Color friendlyColor = new(0.2f, 0.85f, 0.2f);
        public Color enemyColor = new(0.85f, 0.25f, 0.2f);
        public Sprite fallbackAvatar;

        int _phaseCounter;
        TurnEntry[] _turnEntries = Array.Empty<TurnEntry>();
        SlotEntry[] _slotEntries = Array.Empty<SlotEntry>();

        static T AutoFind<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<T>();
#endif
        }

        void Reset() => EnsureDataCapacity();

        void Awake()
        {
            if (!turnManager)
                turnManager = AutoFind<TurnManagerV2>();

            EnsureDataCapacity();
            ClearAll();
        }

        void OnValidate()
        {
            EnsureDataCapacity();
            RefreshTurns();
            RefreshSlots();
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

        void Subscribe()
        {
            if (!turnManager)
                return;

            turnManager.PhaseBegan += OnPhaseBegan;
            turnManager.TurnStarted += OnTurnStarted;
            turnManager.TurnEnded += OnTurnEnded;
        }

        void Unsubscribe()
        {
            if (!turnManager)
                return;

            turnManager.PhaseBegan -= OnPhaseBegan;
            turnManager.TurnStarted -= OnTurnStarted;
            turnManager.TurnEnded -= OnTurnEnded;
        }

        void RebuildInitialState()
        {
            ClearAll();

            if (!turnManager)
                return;

            bool isPlayer = turnManager.IsPlayerPhase;
            int currentPhase = Mathf.Max(1, turnManager.CurrentPhaseIndex);

            if (currentPhase > 0 && _turnEntries.Length > 0)
            {
                var entry = new TurnEntry
                {
                    hasValue = true,
                    isPlayer = isPlayer,
                    phaseIndex = currentPhase
                };
                _turnEntries[0] = entry;
            }

            RefreshTurns();

            var units = turnManager.GetSideUnits(isPlayer);
            if (units != null)
            {
                for (int i = 0; i < units.Count; i++)
                    AddOrRefreshSlot(units[i], isPlayer, false);
                RefreshSlots();
            }

            _phaseCounter = currentPhase;
        }

        void ClearAll()
        {
            ClearTurns();
            ClearSlots();
        }

        void ClearTurns()
        {
            for (int i = 0; i < _turnEntries.Length; i++)
                _turnEntries[i] = default;
            RefreshTurns();
        }

        void ClearSlots()
        {
            for (int i = 0; i < _slotEntries.Length; i++)
                _slotEntries[i] = default;
            RefreshSlots();
        }

        void OnPhaseBegan(bool isPlayerPhase)
        {
            int phaseIndex = turnManager ? turnManager.CurrentPhaseIndex : ++_phaseCounter;
            if (!turnManager)
                _phaseCounter = phaseIndex;

            var entry = new TurnEntry
            {
                hasValue = true,
                isPlayer = isPlayerPhase,
                phaseIndex = Mathf.Max(1, phaseIndex)
            };

            PushTurnEntry(entry);

            var units = turnManager != null ? turnManager.GetSideUnits(isPlayerPhase) : null;
            if (units != null)
            {
                for (int i = 0; i < units.Count; i++)
                    AddOrRefreshSlot(units[i], isPlayerPhase, false);
                RefreshSlots();
            }
        }

        void OnTurnStarted(Unit unit)
        {
            bool isPlayer = turnManager != null && turnManager.IsPlayerUnit(unit);

            if (!TryUpdateSlotEntry(unit, isPlayer, true))
                AddOrRefreshSlot(unit, isPlayer, true);
        }

        void OnTurnEnded(Unit unit)
        {
            RemoveSlotEntry(unit);
        }

        void AddOrRefreshSlot(Unit unit, bool isPlayer, bool refreshImmediately)
        {
            if (unit == null || _slotEntries.Length == 0)
                return;

            RemoveSlotEntry(unit, false);

            var entry = CreateSlotEntry(unit, isPlayer);
            PushSlotEntry(entry);

            if (refreshImmediately)
                RefreshSlots();
        }

        bool TryUpdateSlotEntry(Unit unit, bool isPlayer, bool refreshImmediately)
        {
            if (unit == null || _slotEntries.Length == 0)
                return false;

            string unitId = unit.Id;
            for (int i = 0; i < _slotEntries.Length; i++)
            {
                if (!_slotEntries[i].hasValue)
                    continue;

                if (_slotEntries[i].unit == unit || (!string.IsNullOrEmpty(unitId) && _slotEntries[i].unitId == unitId))
                {
                    var entry = _slotEntries[i];
                    entry.unit = unit;
                    entry.unitId = unitId;
                    entry.isPlayer = isPlayer;
                    entry.avatar = ResolveAvatar(unit);
                    _slotEntries[i] = entry;

                    if (refreshImmediately)
                        RefreshSlots();

                    return true;
                }
            }

            return false;
        }

        void PushTurnEntry(TurnEntry entry)
        {
            if (_turnEntries.Length == 0)
                return;

            for (int i = _turnEntries.Length - 1; i > 0; i--)
                _turnEntries[i] = _turnEntries[i - 1];

            _turnEntries[0] = entry;
            RefreshTurns();
        }

        void PushSlotEntry(SlotEntry entry)
        {
            for (int i = _slotEntries.Length - 1; i > 0; i--)
                _slotEntries[i] = _slotEntries[i - 1];

            if (_slotEntries.Length > 0)
                _slotEntries[0] = entry;
        }

        void RemoveSlotEntry(Unit unit, bool refresh = true)
        {
            if (unit == null || _slotEntries.Length == 0)
            {
                if (refresh)
                    RefreshSlots();
                return;
            }

            string unitId = unit.Id;
            for (int i = 0; i < _slotEntries.Length; i++)
            {
                if (!_slotEntries[i].hasValue)
                    continue;

                if (_slotEntries[i].unit == unit || (!string.IsNullOrEmpty(unitId) && _slotEntries[i].unitId == unitId))
                {
                    for (int j = i; j < _slotEntries.Length - 1; j++)
                        _slotEntries[j] = _slotEntries[j + 1];

                    if (_slotEntries.Length > 0)
                        _slotEntries[_slotEntries.Length - 1] = default;
                    break;
                }
            }

            if (refresh)
                RefreshSlots();
        }

        SlotEntry CreateSlotEntry(Unit unit, bool isPlayer)
        {
            var entry = new SlotEntry
            {
                hasValue = unit != null,
                unit = unit,
                unitId = unit != null ? unit.Id : string.Empty,
                isPlayer = isPlayer,
                avatar = ResolveAvatar(unit)
            };
            return entry;
        }

        Sprite ResolveAvatar(Unit unit)
        {
            if (unit != null && UnitAvatarRegistry.TryGetAvatar(unit, out var sprite) && sprite != null)
                return sprite;
            return fallbackAvatar;
        }

        void RefreshTurns()
        {
            int count = Mathf.Min(turnWidgets.Length, _turnEntries.Length);
            for (int i = 0; i < count; i++)
            {
                var widget = turnWidgets[i];
                if (widget == null)
                    continue;

                var entry = _turnEntries[i];
                if (entry.hasValue)
                    widget.Apply(entry, FormatTurnLabel(entry), entry.isPlayer ? friendlyColor : enemyColor);
                else
                    widget.Clear();
            }

            for (int i = count; i < turnWidgets.Length; i++)
                turnWidgets[i]?.Clear();
        }

        void RefreshSlots()
        {
            int count = Mathf.Min(slotWidgets.Length, _slotEntries.Length);
            for (int i = 0; i < count; i++)
            {
                var widget = slotWidgets[i];
                if (widget == null)
                    continue;

                var entry = _slotEntries[i];
                if (entry.hasValue)
                    widget.Apply(entry, fallbackAvatar);
                else
                    widget.Clear();
            }

            for (int i = count; i < slotWidgets.Length; i++)
                slotWidgets[i]?.Clear();
        }

        static string FormatTurnLabel(in TurnEntry entry)
        {
            string side = entry.isPlayer ? "Player" : "Enemy";
            return $"Turn({side}) {Mathf.Max(1, entry.phaseIndex)}";
        }

        void EnsureDataCapacity()
        {
            int turnLength = turnWidgets != null ? turnWidgets.Length : 0;
            if (_turnEntries == null || _turnEntries.Length != turnLength)
                _turnEntries = new TurnEntry[Mathf.Max(0, turnLength)];

            int slotLength = slotWidgets != null ? slotWidgets.Length : 0;
            if (_slotEntries == null || _slotEntries.Length != slotLength)
                _slotEntries = new SlotEntry[Mathf.Max(0, slotLength)];
        }

        [Serializable]
        public sealed class TurnTimelineTurnWidget
        {
            public GameObject root;
            public TMP_Text label;

            public void Apply(in TurnEntry entry, string text, Color color)
            {
                if (root)
                    root.SetActive(true);

                if (label)
                {
                    label.text = text;
                    label.color = color;
                }
            }

            public void Clear()
            {
                if (label)
                    label.text = string.Empty;

                if (root && !root.activeSelf)
                    root.SetActive(true);
            }
        }

        [Serializable]
        public sealed class TurnTimelineSlotWidget
        {
            public GameObject root;
            public Image avatarImage;
            public TurnTimelineOverlayBinding overlay;

            public void Apply(in SlotEntry entry, Sprite fallback)
            {
                if (root)
                    root.SetActive(true);

                if (avatarImage)
                {
                    var sprite = entry.avatar != null ? entry.avatar : fallback;
                    avatarImage.sprite = sprite;
                    avatarImage.enabled = sprite != null;
                }

                if (overlay != null)
                {
                    var target = avatarImage ? avatarImage.rectTransform : null;
                    overlay.AttachTo(target);
                    overlay.SetText(string.Empty);
                    overlay.HideImmediate();
                }
            }

            public void Clear()
            {
                if (avatarImage)
                {
                    avatarImage.sprite = null;
                    avatarImage.enabled = false;
                }

                if (overlay != null)
                    overlay.Detach();

                if (root && !root.activeSelf)
                    root.SetActive(true);
            }
        }

        public struct TurnEntry
        {
            public bool hasValue;
            public bool isPlayer;
            public int phaseIndex;
        }

        public struct SlotEntry
        {
            public bool hasValue;
            public Unit unit;
            public string unitId;
            public bool isPlayer;
            public Sprite avatar;
        }
    }
}

