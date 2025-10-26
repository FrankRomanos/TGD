using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using TGD.CombatV2;

namespace TGD.UIV2
{
    [DisallowMultipleComponent]
    public sealed class ChainPopupPresenter : MonoBehaviour, IChainPopupUI
    {
        [Header("UI Toolkit")]
        [SerializeField] PanelSettings panelSettings;
        [SerializeField] VisualTreeAsset popupAsset;
        [SerializeField] VisualTreeAsset optionAsset;

        [Header("Display")]
        [SerializeField, Min(0.1f)] float defaultScale = 0.4f;
        [SerializeField] bool allowUxmlScale = false;

        [Header("Placement")]
        [SerializeField] Camera worldCamera;
        [SerializeField] Vector3 worldOffset = new(0f, 2.4f, 0f);
        [SerializeField] Vector2 anchorPadding = new(160f, -48f);
        [SerializeField] float edgePadding = 32f;

        [Header("Fallbacks")]
        [SerializeField] Sprite fallbackIcon;

        UIDocument _document;
        PanelSettings _runtimePanelSettings;
        VisualElement _overlay;
        VisualElement _windowWrap;
        Label _phaseLabel;
        Label _promptLabel;
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
        Transform _anchor;
        Vector3 _anchorWorld;
        bool _hasAnchorWorld;
        float _documentScale = 1f;

        struct OptionEntry
        {
            public VisualElement container;
            public VisualElement root;
            public VisualElement icon;
            public Label name;
            public Label meta;
            public Label key;
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
                _documentScale = Mathf.Max(0.1f, defaultScale);

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
            _visible = false;
            _windowActive = false;
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
                ? Mathf.Max(0.1f, _windowWrap.resolvedStyle.scale.x)
                : 0f;

            if (targetScale <= 0f)
                targetScale = Mathf.Max(0.1f, defaultScale);

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
            defaultScale = Mathf.Max(0.1f, defaultScale);

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

            _pendingSelection = -1;
            _skipRequested = false;
            if (_noneToggle != null)
                _noneToggle.SetValueWithoutNotify(false);

            _windowActive = true;
            _visible = true;

            RefreshSelectionVisuals();

            UpdateAnchorPosition();
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

            RefreshSelectionVisuals();
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
                    key = null
                };

                var check = container.Q<VisualElement>("check");
                if (check != null)
                {
                    var keyLabel = new Label { name = "key" };
                    keyLabel.AddToClassList("opt-key");
                    check.Add(keyLabel);
                    entry.key = keyLabel;
                }

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
                string keyText = FormatKeyLabel(data.Key);
                entry.key.text = keyText;
                entry.key.style.display = string.IsNullOrEmpty(keyText) ? DisplayStyle.None : DisplayStyle.Flex;
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

        static string FormatKeyLabel(KeyCode key)
        {
            if (key == KeyCode.None)
                return string.Empty;

            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
                return ((char)('0' + (key - KeyCode.Alpha0))).ToString();

            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
                return ((char)('0' + (key - KeyCode.Keypad0))).ToString();

            return key.ToString();
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
            _anchor = anchor;
            if (anchor != null)
            {
                _anchorWorld = anchor.position + worldOffset;
                _hasAnchorWorld = true;
            }
            else
            {
                _hasAnchorWorld = false;
            }

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
            if (_document == null)
                return;

            var panel = _document.rootVisualElement?.panel;
            if (panel == null)
                return;

            if (_windowWrap == null)
                return;

            var camera = worldCamera != null ? worldCamera : Camera.main;
            Vector3 worldPos;
            if (_anchor != null)
            {
                worldPos = _anchor.position + worldOffset;
                _anchorWorld = worldPos;
                _hasAnchorWorld = true;
            }
            else if (_hasAnchorWorld)
            {
                worldPos = _anchorWorld;
            }
            else
            {
                worldPos = worldOffset;
            }

            Vector3 screenPos;
            if (camera != null)
                screenPos = camera.WorldToScreenPoint(worldPos);
            else
                screenPos = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 1f);

            if (screenPos.z < 0f)
            {
                _windowWrap.style.display = DisplayStyle.None;
                return;
            }

            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, screenPos);

            float panelWidth = _document.rootVisualElement?.worldBound.width ?? Screen.width;
            float panelHeight = _document.rootVisualElement?.worldBound.height ?? Screen.height;
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

            float panelX = panelPos.x;
            float panelY = panelHeight - panelPos.y;

            float horizontalPadding = Mathf.Max(0f, anchorPadding.x);
            float verticalBias = anchorPadding.y;

            bool placeLeft = screenPos.x > Screen.width * 0.5f;
            float left = placeLeft ? panelX - width - horizontalPadding : panelX + horizontalPadding;
            float top = panelY - (height * 0.5f) + verticalBias;

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
        }
    }
}
