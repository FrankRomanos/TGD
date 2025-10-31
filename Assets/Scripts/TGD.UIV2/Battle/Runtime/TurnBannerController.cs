using UnityEngine;
using TMPro;
using TGD.CombatV2;
using TGD.HexBoard;

namespace TGD.UIV2.Battle
{
    /// <summary>
    /// Simple passive banner for turn/phase announcements.
    /// BattleUIService tells it what to show.
    /// This script MUST NOT subscribe to TurnManagerV2 directly.
    /// </summary>
    public sealed class TurnBannerController : MonoBehaviour
    {
        [Header("UI")]
        public CanvasGroup group;
        public TMP_Text label;

        [Header("Look")]
        public float visibleAlpha = 1f;
        public float hiddenAlpha = 0f;

        bool _isVisible;

        void Awake()
        {
            HideImmediate();
        }

        /// <summary>
        /// Called by BattleUIService when a new phase begins.
        /// Example: Player phase vs Enemy phase.
        /// </summary>
        public void ShowPhaseBegan(bool isPlayerPhase)
        {
            string who = isPlayerPhase ? "Player" : "Enemy";
            ShowText($"Begin Turn ({who})");
        }

        /// <summary>
        /// Called by BattleUIService when a unit actually starts its turn.
        /// </summary>
        public void ShowTurnStarted(Unit unit, bool isPlayerUnit)
        {
            // If you want something else (e.g. unit label), use TurnManagerV2.FormatUnitLabel(unit)
            // But keep fallback safe.
            string unitLabel = (unit != null) ? TurnManagerV2.FormatUnitLabel(unit) : (isPlayerUnit ? "Player" : "Enemy");
            ShowText($"Begin {unitLabel}");
        }

        /// <summary>
        /// Hide instantly, without animation.
        /// </summary>
        public void HideImmediate()
        {
            _isVisible = false;

            if (group != null)
            {
                group.alpha = hiddenAlpha;
                group.blocksRaycasts = false;
                group.interactable = false;
            }

            if (label != null)
                label.text = string.Empty;
        }

        void ShowText(string text)
        {
            _isVisible = true;

            if (label != null)
                label.text = text ?? string.Empty;

            if (group != null)
            {
                group.alpha = visibleAlpha;
                group.blocksRaycasts = false;
                group.interactable = false;
            }

            // (Optional) could start a fade-out coroutine later, but NOT in this task.
        }
    }
}
