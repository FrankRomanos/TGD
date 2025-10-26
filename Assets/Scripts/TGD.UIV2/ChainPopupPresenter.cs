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

        [Header("Placement")]
        [SerializeField] Camera worldCamera;
        [SerializeField] Vector3 worldOffset = new(0f, 2f, 0f);
        [SerializeField] Vector2 panelOffset = new(24f, -160f);

        [Header("Fallbacks")]
        [SerializeField] Sprite fallbackIcon;

        UIDocument _document;
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
        Transform _anchor;
        Vector3 _anchorWorld;
        bool _hasAnchorWorld;

        struct OptionEntry
        {
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
            if (_document != null)
                return;

            _document = GetComponent<UIDocument>();
            if (_document == null)
                _document = gameObject.AddComponent<UIDocument>();

            _document.panelSettings = panelSettings;
            _document.visualTreeAsset = null;

            var root = _document.rootVisualElement;
            root.style.flexGrow = 1f;
            root.style.flexDirection = FlexDirection.Column;
            root.Clear();

            _overlay = popupAsset != null ? popupAsset.CloneTree() : new VisualElement();
            root.Add(_overlay);

            _windowWrap = _overlay.Q<VisualElement>("window-wrap");
            _phaseLabel = _overlay.Q<Label>("phaseLabel");
            _promptLabel = _overlay.Q<Label>("promptLabel");
            _list = _overlay.Q<ScrollView>("list") ?? new ScrollView();
            if (_list.parent == null)
                _overlay.Add(_list);
            _footer = _overlay.Q<VisualElement>("footer");
            _noneLabel = _overlay.Q<Label>("noneLabel");
            _noneToggle = _overlay.Q<Toggle>("noneToggle");

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

        void HideImmediate()
        {
            if (_overlay != null)
                _overlay.style.display = DisplayStyle.None;
            if (_windowWrap != null)
                _windowWrap.style.display = DisplayStyle.None;
            _visible = false;
            _windowActive = false;
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
                if (entry.root != null)
                    entry.root.style.display = DisplayStyle.None;
            }

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
                var element = optionAsset != null ? optionAsset.CloneTree() : new VisualElement();
                var entry = new OptionEntry
                {
                    root = element,
                    icon = element.Q<VisualElement>("icon"),
                    name = element.Q<Label>("name"),
                    meta = element.Q<Label>("meta"),
                    key = null
                };

                var check = element.Q<VisualElement>("check");
                if (check != null)
                {
                    var keyLabel = new Label { name = "key" };
                    keyLabel.AddToClassList("opt-key");
                    check.Add(keyLabel);
                    entry.key = keyLabel;
                }

                element.RegisterCallback<ClickEvent>(_ =>
                {
                    if (!_windowActive)
                        return;
                    if (element.userData is int idx)
                        RequestSelection(idx);
                });

                _list.Add(element);
                _entries.Add(entry);
            }
        }

        OptionEntry UpdateEntry(OptionEntry entry, ChainPopupOptionData data, int index)
        {
            if (entry.root == null)
                return entry;

            entry.root.style.display = DisplayStyle.Flex;
            entry.root.userData = index;
            entry.root.SetEnabled(data.Interactable);

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
        }

        void RequestSelection(int index)
        {
            if (!_windowActive)
                return;

            _pendingSelection = index;
            _skipRequested = false;
            if (_noneToggle != null)
                _noneToggle.SetValueWithoutNotify(false);
        }

        void RequestSkip()
        {
            if (!_windowActive)
                return;

            _skipRequested = true;
            _pendingSelection = -1;
            if (_noneToggle != null)
                _noneToggle.SetValueWithoutNotify(true);
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
            _windowWrap.style.left = panelPos.x + panelOffset.x;
            _windowWrap.style.top = panelPos.y + panelOffset.y;
            _windowWrap.style.display = DisplayStyle.Flex;
        }
    }
}
