using System.Collections;
using System.Collections.Generic;
using TMPro;
using TGD.CombatV2;
using TGD.HexBoard;
using UnityEngine;
using UnityEngine.UI;

namespace TGD.UIV2
{
    /// <summary>
    /// Controls the scrolling turn timeline HUD driven by TurnManagerV2.
    /// </summary>
    [AddComponentMenu("TGD/UI/Turn Timeline Controller")]
    public sealed class TurnTimelineController : MonoBehaviour
    {
        [Header("Runtime Refs")]
        public TurnManagerV2 turnManager;
        public CombatActionManagerV2 combatManager;

        [Header("UI Roots")]
        public RectTransform contentRoot;
        public TimelineSeparatorTemplate separatorTemplate;
        public TimelineSlotTemplate slotTemplate;
        public RectTransform overlayLabelsRoot;

        [Header("Look")]
        public Color friendlyTurnColor = new(0.2f, 0.85f, 0.2f);
        public Color enemyTurnColor = new(0.85f, 0.2f, 0.2f);
        [Min(0f)] public float removalDuration = 0.25f;
        [Min(1f)] public float removalScaleMultiplier = 1.25f;
        public AnimationCurve removalScaleCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        public AnimationCurve removalFadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 0f);

        [Header("Capacity")]
        [Min(1)] public int maxTurnEntries = 2;
        [Min(1)] public int maxSlotEntries = 4;

        [Header("Overlay")]
        public bool disableOverlayLabelsOnAwake = true;

        readonly List<TimelineItem> _items = new();
        readonly List<SlotEntry> _slotEntries = new();
        readonly Dictionary<int, TurnEntry> _turnEntries = new();
        readonly Dictionary<int, int> _slotsPerPhase = new();
        readonly Dictionary<RectTransform, Coroutine> _removalRoutines = new();

        int _lastPhaseIndex;

        [System.Serializable]
        public sealed class TimelineSeparatorTemplate
        {
            public RectTransform root;
            public TMP_Text label;
            public CanvasGroup canvas;
            public RectTransform spacer;
        }

        [System.Serializable]
        public sealed class TimelineSlotTemplate
        {
            public RectTransform root;
            public CanvasGroup canvas;
            public Image avatarImage;
            public TMP_Text nameLabel;
            public RectTransform overlayLabelTemplate;
            public Sprite fallbackAvatar;
            public bool hideAvatarWhenMissing = true;
        }

        abstract class TimelineItem
        {
            public RectTransform rect;
            public CanvasGroup canvas;
            public int phaseIndex;
        }

        sealed class TurnEntry : TimelineItem
        {
            public TMP_Text label;
            public bool isPlayer;
            public RectTransform spacer;
        }

        sealed class SlotEntry : TimelineItem
        {
            public Unit unit;
            public bool isPlayer;
            public Image avatar;
            public TMP_Text caption;
            public RectTransform overlayLabel;
        }

        static T AutoFind<T>() where T : Object
        {
    #if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<T>(FindObjectsInactive.Include);
    #else
            return FindObjectOfType<T>();
    #endif
        }

        void Awake()
        {
            if (!turnManager)
                turnManager = AutoFind<TurnManagerV2>();
            if (!combatManager)
                combatManager = AutoFind<CombatActionManagerV2>();

            PrepareTemplate(separatorTemplate?.root, transform);
            PrepareTemplate(separatorTemplate?.spacer, transform);
            PrepareTemplate(slotTemplate?.root, transform);
            PrepareTemplate(slotTemplate?.overlayLabelTemplate, overlayLabelsRoot ? overlayLabelsRoot : transform);

            if (overlayLabelsRoot && disableOverlayLabelsOnAwake)
                overlayLabelsRoot.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            Subscribe();
            if (turnManager)
                _lastPhaseIndex = Mathf.Max(1, turnManager.CurrentPhaseIndex);
        }

        void OnDisable()
        {
            Unsubscribe();
            ClearTimeline(true);
        }

        void Subscribe()
        {
            if (turnManager != null)
            {
                turnManager.PhaseBegan += OnPhaseBegan;
                turnManager.TurnStarted += OnTurnStarted;
                turnManager.TurnEnded += OnTurnEnded;
            }
        }

        void Unsubscribe()
        {
            if (turnManager != null)
            {
                turnManager.PhaseBegan -= OnPhaseBegan;
                turnManager.TurnStarted -= OnTurnStarted;
                turnManager.TurnEnded -= OnTurnEnded;
            }
        }

        void LateUpdate()
        {
            SyncOverlayLabels();
        }

        void OnPhaseBegan(bool isPlayerPhase)
        {
            int phaseIndex = turnManager != null ? turnManager.CurrentPhaseIndex : _lastPhaseIndex + 1;
            _lastPhaseIndex = Mathf.Max(_lastPhaseIndex, phaseIndex);
            EnsureTurnEntry(phaseIndex, isPlayerPhase);
        }

        void OnTurnStarted(Unit unit)
        {
            if (unit == null)
                return;

            int phaseIndex = turnManager != null ? turnManager.CurrentPhaseIndex : _lastPhaseIndex;
            bool isPlayer = turnManager != null ? turnManager.IsPlayerUnit(unit) : false;
            var entry = EnsureTurnEntry(phaseIndex, isPlayer);
            if (entry == null)
                return;

            CreateSlotEntry(unit, phaseIndex, isPlayer);
        }

        void OnTurnEnded(Unit unit)
        {
            if (unit == null)
                return;

            int phaseIndex = turnManager != null ? turnManager.CurrentPhaseIndex : _lastPhaseIndex;
            SlotEntry target = null;
            for (int i = 0; i < _slotEntries.Count; i++)
            {
                var slot = _slotEntries[i];
                if (slot == null || slot.unit != unit)
                    continue;
                if (slot.phaseIndex == phaseIndex)
                {
                    target = slot;
                    break;
                }
                if (target == null)
                    target = slot; // fallback to first match if phase differs
            }

            if (target == null)
                return;

            RemoveSlot(target, false);
            ApplySiblingIndices();
            SyncOverlayLabels();
        }

        TurnEntry EnsureTurnEntry(int phaseIndex, bool isPlayerPhase)
        {
            if (phaseIndex <= 0)
                phaseIndex = Mathf.Max(1, _lastPhaseIndex);

            if (_turnEntries.TryGetValue(phaseIndex, out var existing) && existing != null)
                return existing;

            if (separatorTemplate == null || separatorTemplate.root == null || contentRoot == null)
                return null;

            var clone = InstantiateTemplate(separatorTemplate.root, contentRoot);
            var entry = new TurnEntry
            {
                rect = clone,
                canvas = PrepareCanvas(clone, separatorTemplate.canvas),
                phaseIndex = phaseIndex,
                isPlayer = isPlayerPhase,
                label = FindCloneComponent(clone, separatorTemplate.label)
            };
            if (separatorTemplate.spacer)
            {
                entry.spacer = InstantiateTemplate(separatorTemplate.spacer, contentRoot);
            }

            ApplyTurnLabel(entry);
            _turnEntries[phaseIndex] = entry;
            _slotsPerPhase[phaseIndex] = 0;
            InsertItem(entry, 0);
            ApplySiblingIndices();
            TrimToCapacity();
            return entry;
        }

        void CreateSlotEntry(Unit unit, int phaseIndex, bool isPlayerPhase)
        {
            if (slotTemplate == null || slotTemplate.root == null || contentRoot == null)
                return;

            var clone = InstantiateTemplate(slotTemplate.root, contentRoot);
            var slot = new SlotEntry
            {
                rect = clone,
                canvas = PrepareCanvas(clone, slotTemplate.canvas),
                phaseIndex = phaseIndex,
                unit = unit,
                isPlayer = isPlayerPhase,
                avatar = FindCloneComponent(clone, slotTemplate.avatarImage),
                caption = FindCloneComponent(clone, slotTemplate.nameLabel)
            };

            if (slot.caption)
                slot.caption.text = TurnManagerV2.FormatUnitLabel(unit);

            Sprite portrait = ResolveAvatar(unit);
            if (slot.avatar)
            {
                slot.avatar.sprite = portrait;
                slot.avatar.enabled = portrait != null || !slotTemplate.hideAvatarWhenMissing;
            }

            if (overlayLabelsRoot && slotTemplate.overlayLabelTemplate)
            {
                slot.overlayLabel = InstantiateTemplate(slotTemplate.overlayLabelTemplate, overlayLabelsRoot);
                if (slot.overlayLabel)
                    slot.overlayLabel.gameObject.SetActive(true);

                var overlayText = slot.overlayLabel ? slot.overlayLabel.GetComponentInChildren<TMP_Text>(true) : null;
                if (overlayText && slot.caption)
                    overlayText.text = slot.caption.text;
            }

            if (_slotsPerPhase.TryGetValue(phaseIndex, out var count))
                _slotsPerPhase[phaseIndex] = count + 1;
            else
                _slotsPerPhase[phaseIndex] = 1;

            int insertIndex = DetermineSlotInsertIndex(phaseIndex);
            _slotEntries.Add(slot);
            InsertItem(slot, insertIndex);
            ApplySiblingIndices();
            TrimToCapacity();
            SyncOverlayLabels();
        }

        int DetermineSlotInsertIndex(int phaseIndex)
        {
            int insertIndex = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item.phaseIndex == phaseIndex)
                    insertIndex = i + 1;
            }
            return insertIndex;
        }

        void RemoveSlot(SlotEntry slot, bool immediate)
        {
            RemoveSlot(slot, immediate, true);
        }

        void RemoveSlot(SlotEntry slot, bool immediate, bool allowTurnCleanup)
        {
            if (slot == null)
                return;

            if (_slotEntries.Contains(slot))
                _slotEntries.Remove(slot);
            _items.Remove(slot);

            if (_slotsPerPhase.TryGetValue(slot.phaseIndex, out var count))
            {
                count = Mathf.Max(0, count - 1);
                if (count <= 0)
                {
                    _slotsPerPhase.Remove(slot.phaseIndex);
                    if (allowTurnCleanup && _turnEntries.TryGetValue(slot.phaseIndex, out var turn))
                    {
                        _turnEntries.Remove(slot.phaseIndex);
                        RemoveTurnEntry(turn, immediate);
                    }
                }
                else
                {
                    _slotsPerPhase[slot.phaseIndex] = count;
                }
            }

            if (slot.overlayLabel)
            {
                Destroy(slot.overlayLabel.gameObject);
                slot.overlayLabel = null;
            }

            StartRemovalAnimation(slot.rect, slot.canvas, immediate);
        }

        void RemoveTurnEntry(TurnEntry entry, bool immediate)
        {
            if (entry == null)
                return;

            _items.Remove(entry);
            StartRemovalAnimation(entry.rect, entry.canvas, immediate);
            if (entry.spacer)
                StartRemovalAnimation(entry.spacer, null, immediate);
        }

        void RemovePhase(int phaseIndex, bool immediate)
        {
            for (int i = _slotEntries.Count - 1; i >= 0; i--)
            {
                var slot = _slotEntries[i];
                if (slot != null && slot.phaseIndex == phaseIndex)
                    RemoveSlot(slot, immediate, false);
            }

            if (_turnEntries.TryGetValue(phaseIndex, out var turn))
            {
                _turnEntries.Remove(phaseIndex);
                RemoveTurnEntry(turn, immediate);
            }
      
            ApplySiblingIndices();
        }

        void TrimToCapacity()
        {
            while (_slotEntries.Count > maxSlotEntries)
            {
                var slot = FindBottomSlot();
                if (slot == null)
                    break;
                RemoveSlot(slot, false);
            }

            while (_turnEntries.Count > maxTurnEntries)
            {
                var turn = FindOldestTurnEntry();
                if (turn == null)
                    break;
                RemovePhase(turn.phaseIndex, false);
            }
      
            ApplySiblingIndices();
        }

        SlotEntry FindBottomSlot()
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i] is SlotEntry slot)
                    return slot;
            }
            return null;
        }

        TurnEntry FindOldestTurnEntry()
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i] is TurnEntry turn)
                    return turn;
            }
            return null;
        }

        void InsertItem(TimelineItem item, int index)
        {
            if (item == null)
                return;

            index = Mathf.Clamp(index, 0, _items.Count);
            _items.Insert(index, item);
        }

        void ApplySiblingIndices()
        {
            if (!contentRoot)
                return;

            int sibling = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item?.rect == null)
                    continue;
                item.rect.SetSiblingIndex(sibling);
                sibling++;
                if (item is TurnEntry turn && turn.spacer)
                {
                    turn.spacer.SetSiblingIndex(sibling);
                    sibling++;
                }
            }
        }

        void SyncOverlayLabels()
        {
            if (!overlayLabelsRoot || !overlayLabelsRoot.gameObject.activeInHierarchy)
                return;

            foreach (var slot in _slotEntries)
            {
                if (slot == null || slot.overlayLabel == null)
                    continue;

                RectTransform target = slot.avatar != null ? slot.avatar.rectTransform : slot.rect;
                if (!target)
                    continue;

                Vector3 world = target.TransformPoint(target.rect.center);
                slot.overlayLabel.position = world;
            }
        }

        void ClearTimeline(bool immediate)
        {
            for (int i = _slotEntries.Count - 1; i >= 0; i--)
                RemoveSlot(_slotEntries[i], immediate, false);
            _slotEntries.Clear();

            foreach (var turn in _turnEntries.Values)
                RemoveTurnEntry(turn, immediate);
            _turnEntries.Clear();
            _slotsPerPhase.Clear();
            _items.Clear();

            if (immediate)
            {
                foreach (var routine in _removalRoutines.Values)
                {
                    if (routine != null)
                        StopCoroutine(routine);
                }
                _removalRoutines.Clear();
            }
        }

        void StartRemovalAnimation(RectTransform rect, CanvasGroup canvas, bool immediate)
        {
            if (!rect)
                return;

            if (immediate || removalDuration <= 0f || !isActiveAndEnabled)
            {
                if (canvas)
                    canvas.alpha = 0f;
                Destroy(rect.gameObject);
                return;
            }

            if (_removalRoutines.TryGetValue(rect, out var routine) && routine != null)
                StopCoroutine(routine);

            _removalRoutines[rect] = StartCoroutine(RemovalRoutine(rect, canvas));
        }

        IEnumerator RemovalRoutine(RectTransform rect, CanvasGroup canvas)
        {
            float duration = Mathf.Max(0.01f, removalDuration);
            float elapsed = 0f;
            Vector3 baseScale = rect.localScale;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float scaleT = removalScaleCurve != null ? removalScaleCurve.Evaluate(t) : t;
                float fadeT = removalFadeCurve != null ? removalFadeCurve.Evaluate(t) : t;
                float scale = Mathf.Lerp(1f, removalScaleMultiplier, scaleT);
                if (rect)
                    rect.localScale = baseScale * scale;
                if (canvas)
                    canvas.alpha = Mathf.Lerp(1f, 0f, fadeT);
                yield return null;
            }

            if (rect)
                Destroy(rect.gameObject);
            _removalRoutines.Remove(rect);
        }

        static RectTransform InstantiateTemplate(RectTransform template, Transform parent)
        {
            if (!template || !parent)
                return null;

            var clone = Instantiate(template);
            clone.gameObject.SetActive(true);
            clone.SetParent(parent, false);
            clone.localScale = Vector3.one;
            clone.localRotation = Quaternion.identity;
            clone.anchoredPosition3D = Vector3.zero;
            return clone;
        }

        static CanvasGroup PrepareCanvas(RectTransform target, CanvasGroup template)
        {
            if (!target)
                return null;

            var canvas = target.GetComponent<CanvasGroup>();
            if (!canvas)
                canvas = target.gameObject.AddComponent<CanvasGroup>();

            if (template)
            {
                canvas.interactable = template.interactable;
                canvas.blocksRaycasts = template.blocksRaycasts;
            }
            else
            {
                canvas.interactable = false;
                canvas.blocksRaycasts = false;
            }
            canvas.alpha = 1f;
            return canvas;
        }

        static TComponent FindCloneComponent<TComponent>(RectTransform cloneRoot, TComponent template)
            where TComponent : Component
        {
            if (!cloneRoot)
                return null;

            if (!template)
            {
                var components = cloneRoot.GetComponentsInChildren<TComponent>(true);
                return components.Length > 0 ? components[0] : null;
            }

            string targetName = template.name;
            var clones = cloneRoot.GetComponentsInChildren<TComponent>(true);
            for (int i = 0; i < clones.Length; i++)
            {
                if (clones[i] != null && clones[i].name == targetName)
                    return clones[i];
            }

            return clones.Length > 0 ? clones[0] : null;
        }

        static void PrepareTemplate(RectTransform template, Transform targetParent)
        {
            if (!template)
                return;

            template.gameObject.SetActive(false);
            if (targetParent && template.parent == targetParent)
                return;

            if (targetParent)
                template.SetParent(targetParent, false);
        }

        Sprite ResolveAvatar(Unit unit)
        {
            if (unit != null && TurnTimelineAvatarRegistry.TryGetAvatar(unit.Id, out var sprite) && sprite)
                return sprite;
            return slotTemplate != null ? slotTemplate.fallbackAvatar : null;
        }

        void ApplyTurnLabel(TurnEntry entry)
        {
            if (entry?.label == null)
                return;

            string side = entry.isPlayer ? "Player" : "Enemy";
            entry.label.text = $"Turn({side}) {entry.phaseIndex}";
            entry.label.color = entry.isPlayer ? friendlyTurnColor : enemyTurnColor;
        }
    }
}
