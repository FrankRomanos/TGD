using System.Collections.Generic;
using UnityEngine;

namespace TGD.CombatV2
{
    public readonly struct ChainPopupOptionData
    {
        public ChainPopupOptionData(
            string id,
            string name,
            string meta,
            Sprite icon,
            KeyCode key,
            bool interactable = true,
            bool startsGroup = false,
            string groupLabel = null)
        {
            Id = id;
            Name = name;
            Meta = meta;
            Icon = icon;
            Key = key;
            Interactable = interactable;
            StartsGroup = startsGroup;
            GroupLabel = groupLabel;
        }

        public string Id { get; }
        public string Name { get; }
        public string Meta { get; }
        public Sprite Icon { get; }
        public KeyCode Key { get; }
        public bool Interactable { get; }
        public bool StartsGroup { get; }
        public string GroupLabel { get; }
    }

    public readonly struct ChainPopupStageData
    {
        public ChainPopupStageData(string label, IReadOnlyList<ChainPopupOptionData> options, bool showSkip)
        {
            Label = label;
            Options = options;
            ShowSkip = showSkip;
        }

        public string Label { get; }
        public IReadOnlyList<ChainPopupOptionData> Options { get; }
        public bool ShowSkip { get; }
    }

    public readonly struct ChainPopupWindowData
    {
        public ChainPopupWindowData(string header, string prompt, bool isEnemyPhase, string context = null)
        {
            Header = header;
            Prompt = prompt;
            IsEnemyPhase = isEnemyPhase;
            Context = context;
        }

        public string Header { get; }
        public string Prompt { get; }
        public bool IsEnemyPhase { get; }
        public string Context { get; }
    }

    public interface IChainPopupUI
    {
        void OpenWindow(ChainPopupWindowData window);
        void CloseWindow();
        void UpdateStage(ChainPopupStageData stage);
        bool TryConsumeSelection(out int index);
        bool TryConsumeSkip();
        void SetAnchor(Transform anchor);
    }
}
