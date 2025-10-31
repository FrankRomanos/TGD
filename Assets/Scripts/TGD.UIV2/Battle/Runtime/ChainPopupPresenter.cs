using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using TGD.AudioV2;
using TGD.CombatV2;

namespace TGD.UIV2.Battle
{
    [DisallowMultipleComponent]
    public sealed class ChainPopupPresenter : MonoBehaviour, IChainPopupUI
    {
        [Header("UI Toolkit")]
        [SerializeField] PanelSettings panelSettings;
        [SerializeField] VisualTreeAsset popupAsset;
        [SerializeField] VisualTreeAsset optionAsset;
        public BattleAudioManager audioManager; // <-- 新增：实例引用，不再用静态

        CombatActionManagerV2 _combat;
        bool _isOpen;

        [Header("Display")]
        [SerializeField, Min(0.1f)] float defaultScale = 1f;
        [SerializeField] bool allowUxmlScale = false;

        [Header("Placement")]
        [SerializeField] Vector2 screenAnchor = new(0.72f, 0.38f);
        [SerializeField] Vector2 screenOffset = new(-28f, 20f);
        [SerializeField] Vector2 windowPivot = new(0.5f, 0.5f);
        [SerializeField] float edgePadding = 24f;

        [Header("Fallbacks")]
        [SerializeField] Sprite fallbackIcon;

        UIDocument _document;
        PanelSettings _runtimePanelSettings;
        VisualElement _overlay;
        VisualElement _windowWrap;
        Label _phaseLabel;
        Label _promptLabel;
        Label _contextLabel;
        ScrollView _list;
        VisualElement _footer;
        Label _noneLabel;
        Toggle _noneToggle;

        readonly List<OptionEntry> _entries = new();
        readonly List<ChainPopupOptionData> _stageOptions = new();

        int _pendingSelection = -1;
        bool _skipRequested;
        bool _windowActive;
        bool _visible;
        bool _listPrepared;
        bool _scaleInitialized;
        float _documentScale = 1f;
        // 让 BattleUIService 来注入依赖，而不是自己乱找
        public void Initialize(CombatActionManagerV2 combatMgr, BattleAudioManager audioMgr)
        {
            _combat = combatMgr;
            audioManager = audioMgr;
        }

        struct OptionEntry
        {
            public VisualElement container;
            public VisualElement root;
            public VisualElement icon;
            public Label name;
            public Label meta;
            public Label key;
            public VisualElement groupHeader;
            public Label groupLabel;
        }

        void Awake()
        {
            EnsureDocument();
            HideImmediate();
        }

        void OnEnable()
        {
            EnsureDocument();
            HideImmediate();
        }

        void EnsureDocument()
        {
            if (!_scaleInitialized)
                _documentScale = Mathf.Max(1f, defaultScale);

            if (_document == null)
            {
                _document = GetComponent<UIDocument>();
                if (_document == null)
                    _document = gameObject.AddComponent<UIDocument>();
            }

            if (panelSettings != null && _runtimePanelSettings == null)
            {
                _runtimePanelSettings = Instantiate(panelSettings);
                _runtimePanelSettings.name = panelSettings.name + " (Runtime)";
                _runtimePanelSettings.hideFlags = HideFlags.DontSave;
            }

            if (_runtimePanelSettings != null)
            {
                _runtimePanelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
                _document.panelSettings = _runtimePanelSettings;
            }
            else if (_document.panelSettings != null)
            {
                _runtimePanelSettings = _document.panelSettings;
                _runtimePanelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            }

            if (_overlay == null)
            {
                if (_document.visualTreeAsset == null && popupAsset != null)
                    _document.visualTreeAsset = popupAsset;

                var root = _document.rootVisualElement;
                if (root.childCount == 0)
                {
                    if (popupAsset != null)
                        popupAsset.CloneTree(root);
                    else
                        root.Add(new VisualElement { name = "overlay" });
                }

                _overlay = root.Q<VisualElement>("overlay") ?? root.Q<VisualElement>(className: "overlay");
                if (_overlay == null && popupAsset != null)
                {
                    root.Clear();
                    popupAsset.CloneTree(root);
                    _overlay = root.Q<VisualElement>("overlay") ?? root.Q<VisualElement>(className: "overlay");
                }

                _windowWrap = root.Q<VisualElement>("window-wrap");
                _phaseLabel = root.Q<Label>("phaseLabel");
                _promptLabel = root.Q<Label>("promptLabel");
                _contextLabel = root.Q<Label>("contextLabel");
                _list = root.Q<ScrollView>("list");
                _footer = root.Q<VisualElement>("footer");
                _noneLabel = root.Q<Label>("noneLabel");
                _noneToggle = root.Q<Toggle>("noneToggle");

                if (_windowWrap != null)
                {
                    _windowWrap.style.position = Position.Absolute;
                    _windowWrap.style.left = 0f;
                    _windowWrap.style.top = 0f;
                    _windowWrap.style.right = StyleKeyword.Null;
                    _windowWrap.style.bottom = StyleKeyword.Null;
                    if (!_scaleInitialized && allowUxmlScale)
                        _windowWrap.RegisterCallback<GeometryChangedEvent>(HandleWindowWrapGeometry);
                    else if (!_scaleInitialized)
                        InitializeWindowScale();
                }

                if (_list != null && !_listPrepared)
                {
                    _list.Clear();
                    _listPrepared = true;
                }

                if (_noneLabel != null)
                    _noneLabel.RegisterCallback<ClickEvent>(_ => RequestSkip());
                if (_noneToggle != null)
                    _noneToggle.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.newValue)
                            RequestSkip();
                    });

                HideImmediate();
            }

            if (_scaleInitialized)
                ApplyPanelScale();
        }

        void HideImmediate()
        {
            if (_overlay != null)
                _overlay.style.display = DisplayStyle.None;
            if (_windowWrap != null)
                _windowWrap.style.display = DisplayStyle.None;
            if (_contextLabel != null)
            {
                _contextLabel.text = string.Empty;
                _contextLabel.style.display = DisplayStyle.None;
            }
            _visible = false;
            _windowActive = false;
            ChainPopupState.NotifyVisibility(false);
        }

        void HandleWindowWrapGeometry(GeometryChangedEvent evt)
        {
            if (_windowWrap == null)
                return;

            _windowWrap.UnregisterCallback<GeometryChangedEvent>(HandleWindowWrapGeometry);
            InitializeWindowScale();
        }

        void InitializeWindowScale()
        {
            float targetScale = allowUxmlScale && _windowWrap != null
                ? Mathf.Max(0.1f, _windowWrap.resolvedStyle.scale.value.x)
                : 0f;

            if (targetScale <= 0f)
                targetScale = Mathf.Max(1f, defaultScale);

            _documentScale = targetScale;
            _scaleInitialized = true;

            if (_windowWrap != null)
                _windowWrap.transform.scale = Vector3.one;

            ApplyPanelScale();
        }

        void ApplyPanelScale()
        {
            float scale = Mathf.Max(0.1f, _documentScale);

            var targetSettings = _runtimePanelSettings != null ? _runtimePanelSettings : _document != null ? _document.panelSettings : null;
            if (targetSettings != null)
                targetSettings.scale = scale;

            if (_overlay != null)
                _overlay.transform.scale = Vector3.one;

            if (_windowWrap != null)
                _windowWrap.transform.scale = Vector3.one;
        }

        void OnValidate()
        {
            defaultScale = Mathf.Max(1f, defaultScale);

            if (!Application.isPlaying)
            {
                _scaleInitialized = false;
                _documentScale = defaultScale;
            }
            else if (_scaleInitialized && !allowUxmlScale)
            {
                _documentScale = defaultScale;
                ApplyPanelScale();
            }
        }

        public void OpenWindow(ChainPopupWindowData window)
        {
            EnsureDocument();
            if (_overlay == null)
                return;

            _overlay.style.display = DisplayStyle.Flex;
            if (_windowWrap != null)
                _windowWrap.style.display = DisplayStyle.Flex;

            if (_phaseLabel != null)
                _phaseLabel.text = window.Header ?? string.Empty;
            if (_promptLabel != null)
                _promptLabel.text = window.Prompt ?? string.Empty;
            if (_contextLabel != null)
            {
                string context = window.Context ?? string.Empty;
                _contextLabel.text = context;
                _contextLabel.style.display = string.IsNullOrEmpty(context)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }

            _pendingSelection = -1;
            _skipRequested = false;
            if (_noneToggle != null)
                _noneToggle.SetValueWithoutNotify(false);

            _windowActive = true;
            _visible = true;

            RefreshSelectionVisuals();

            UpdateAnchorPosition();
            if (audioManager != null)
                audioManager.PlayEvent(BattleAudioEvent.ChainPopupOpen);
            ChainPopupState.NotifyVisibility(true);
            Debug.Log($"[ChainPopup] OpenWindow() overlay={_overlay != null}, windowWrap={_windowWrap != null}");
        }

        public void CloseWindow()
        {
            _windowActive = false;
            _visible = false;
            _pendingSelection = -1;
            _skipRequested = false;
            if (_noneToggle != null)
                _noneToggle.SetValueWithoutNotify(false);
            if (_overlay != null)
                _overlay.style.display = DisplayStyle.None;
            if (_windowWrap != null)
                _windowWrap.style.display = DisplayStyle.None;
            if (_contextLabel != null)
            {
                _contextLabel.text = string.Empty;
                _contextLabel.style.display = DisplayStyle.None;
            }

            RefreshSelectionVisuals();
            ChainPopupState.NotifyVisibility(false);
        }

        public void UpdateStage(ChainPopupStageData stage)
        {
            EnsureDocument();
            if (_list == null)
                return;

            CopyOptions(stage.Options);
            EnsureEntryCount(_stageOptions.Count);

            for (int i = 0; i < _stageOptions.Count; i++)
            {
                var entry = _entries[i];
                entry = UpdateEntry(entry, _stageOptions[i], i);
                _entries[i] = entry;
            }

            for (int i = _stageOptions.Count; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.container != null)
                    entry.container.style.display = DisplayStyle.None;
            }

            RefreshSelectionVisuals();

            if (_footer != null)
                _footer.style.display = stage.ShowSkip ? DisplayStyle.Flex : DisplayStyle.None;

            if (!stage.ShowSkip && _noneToggle != null)
                _noneToggle.SetValueWithoutNotify(false);
        }

        void CopyOptions(IReadOnlyList<ChainPopupOptionData> options)
        {
            _stageOptions.Clear();
            if (options == null)
                return;

            for (int i = 0; i < options.Count; i++)
                _stageOptions.Add(options[i]);
        }

        void EnsureEntryCount(int count)
        {
            for (int i = _entries.Count; i < count; i++)
            {
                if (_list == null)
                    break;

                var container = CreateOptionElement();
                if (container == null)
                    break;

                var root = container.Q<VisualElement>("root") ?? container;
                var entry = new OptionEntry
                {
                    container = container,
                    root = root,
                    icon = container.Q<VisualElement>("icon"),
                    name = container.Q<Label>("name"),
                    meta = container.Q<Label>("meta"),
                    key = container.Q<Label>("key"),
                    groupHeader = container.Q<VisualElement>("groupHeader"),
                    groupLabel = container.Q<Label>("groupLabel")
                };

                if (entry.groupHeader != null)
                    entry.groupHeader.style.display = DisplayStyle.None;
                if (entry.groupLabel != null)
                    entry.groupLabel.style.display = DisplayStyle.None;

                if (entry.key != null && !entry.key.ClassListContains("opt-key"))
                    entry.key.AddToClassList("opt-key");

                container.RegisterCallback<ClickEvent>(_ =>
                {
                    if (!_windowActive)
                        return;
                    if (container.userData is int idx)
                        RequestSelection(idx);
                });

                _list.Add(container);
                _entries.Add(entry);
            }
        }

        OptionEntry UpdateEntry(OptionEntry entry, ChainPopupOptionData data, int index)
        {
            if (entry.root == null)
                return entry;

            if (entry.container != null)
            {
                entry.container.style.display = DisplayStyle.Flex;
                entry.container.userData = index;
            }

            entry.root.SetEnabled(data.Interactable);
            entry.root.EnableInClassList("disabled", !data.Interactable);
            entry.root.EnableInClassList("group-start", data.StartsGroup);

            if (entry.groupHeader != null)
            {
                bool showHeader = data.StartsGroup;
                entry.groupHeader.style.display = showHeader ? DisplayStyle.Flex : DisplayStyle.None;

                if (entry.groupLabel != null)
                {
                    if (!string.IsNullOrEmpty(data.GroupLabel) && showHeader)
                    {
                        entry.groupLabel.text = data.GroupLabel;
                        entry.groupLabel.style.display = DisplayStyle.Flex;
                    }
                    else
                    {
                        entry.groupLabel.text = string.Empty;
                        entry.groupLabel.style.display = DisplayStyle.None;
                    }
                }
            }

            string displayName = string.IsNullOrEmpty(data.Name) ? data.Id : data.Name;
            if (entry.name != null)
                entry.name.text = displayName ?? string.Empty;

            if (entry.meta != null)
            {
                if (string.IsNullOrEmpty(data.Meta))
                {
                    entry.meta.text = string.Empty;
                    entry.meta.style.display = DisplayStyle.None;
                }
                else
                {
                    entry.meta.text = data.Meta;
                    entry.meta.style.display = DisplayStyle.Flex;
                }
            }

            if (entry.icon != null)
            {
                var sprite = data.Icon != null ? data.Icon : fallbackIcon;
                if (sprite != null)
                {
                    entry.icon.style.backgroundImage = new StyleBackground(sprite);
                    entry.icon.style.display = DisplayStyle.Flex;
                }
                else
                {
                    entry.icon.style.backgroundImage = StyleKeyword.Null;
                    entry.icon.style.display = DisplayStyle.None;
                }
            }

            if (entry.key != null)
            {
                entry.key.text = string.Empty;
                entry.key.style.display = DisplayStyle.None;
            }

            return entry;
        }

        VisualElement CreateOptionElement()
        {
            if (optionAsset != null)
                return optionAsset.CloneTree();

            if (popupAsset != null)
            {
                var temp = popupAsset.CloneTree();
                var candidate = temp.Q<VisualElement>(className: "option-root") ?? temp.Q<VisualElement>("root");
                if (candidate != null)
                {
                    candidate.RemoveFromHierarchy();
                    return candidate;
                }
            }

            return new VisualElement();
        }

        public bool TryConsumeSelection(out int index)
        {
            if (_pendingSelection >= 0)
            {
                index = _pendingSelection;
                _pendingSelection = -1;
                if (_noneToggle != null)
                    _noneToggle.SetValueWithoutNotify(false);
                return true;
            }

            index = -1;
            return false;
        }

        public bool TryConsumeSkip()
        {
            if (_skipRequested)
            {
                _skipRequested = false;
                if (_noneToggle != null)
                    _noneToggle.SetValueWithoutNotify(false);
                return true;
            }

            return false;
        }

        public void SetAnchor(Transform anchor)
        {
            if (_visible)
                UpdateAnchorPosition();
        }

        void RequestSelection(int index)
        {
            if (!_windowActive)
                return;

            _pendingSelection = index;
            _skipRequested = false;
            if (_noneToggle != null)
                _noneToggle.SetValueWithoutNotify(false);

            RefreshSelectionVisuals();
        }

        void RequestSkip()
        {
            if (!_windowActive)
                return;

            _skipRequested = true;
            _pendingSelection = -1;
            if (_noneToggle != null)
                _noneToggle.SetValueWithoutNotify(true);

            RefreshSelectionVisuals();
        }

        void LateUpdate()
        {
            if (!_visible || _overlay == null)
                return;

            UpdateAnchorPosition();
        }

        void UpdateAnchorPosition()
        {
            if (_document == null || _windowWrap == null)
                return;

            var root = _document.rootVisualElement;
            if (root == null)
                return;

            float panelWidth = root.worldBound.width;
            float panelHeight = root.worldBound.height;
            if (panelWidth <= 0f)
                panelWidth = Screen.width;
            if (panelHeight <= 0f)
                panelHeight = Screen.height;

            Rect bounds = _windowWrap.worldBound;
            float width = bounds.width;
            float height = bounds.height;
            if (width <= 0f)
                width = _windowWrap.resolvedStyle.width;
            if (height <= 0f)
                height = _windowWrap.resolvedStyle.height;
            if (width <= 0f)
                width = 560f;
            if (height <= 0f)
                height = 240f;

            Vector2 anchor = new(
                Mathf.Clamp01(screenAnchor.x),
                Mathf.Clamp01(screenAnchor.y));

            float pivotX = Mathf.Clamp01(windowPivot.x);
            float pivotY = Mathf.Clamp01(windowPivot.y);

            float anchorX = panelWidth * anchor.x;
            float anchorY = panelHeight * (1f - anchor.y);

            float left = anchorX - width * pivotX + screenOffset.x;
            float top = anchorY - height * pivotY + screenOffset.y;

            float margin = Mathf.Max(0f, edgePadding);
            left = Mathf.Clamp(left, margin, panelWidth - width - margin);
            top = Mathf.Clamp(top, margin, panelHeight - height - margin);

            _windowWrap.style.left = left;
            _windowWrap.style.top = top;
            _windowWrap.style.display = DisplayStyle.Flex;
        }

        void RefreshSelectionVisuals()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.root == null)
                    continue;

                bool isSelected = _pendingSelection == i;
                entry.root.EnableInClassList("selected", isSelected);
            }
        }

        void OnDestroy()
        {
            if (_runtimePanelSettings != null && _runtimePanelSettings != panelSettings)
            {
                if (Application.isPlaying)
                    Destroy(_runtimePanelSettings);
                else
                    DestroyImmediate(_runtimePanelSettings);
            }

            _runtimePanelSettings = null;
            ChainPopupState.NotifyVisibility(false);
        }
    }
}
