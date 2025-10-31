using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using TGD.CombatV2;
using TGD.HexBoard;

namespace TGD.UIV2.Battle
{
    /// <summary>
    /// Passive banner for turn/phase announcements.
    /// BattleUIService tells it what to show.
    /// This script MUST NOT subscribe to TurnManagerV2 directly.
    /// </summary>
    public sealed class TurnBannerController : MonoBehaviour
    {
        struct BannerEntry
        {
            public readonly string Text;
            public readonly Color Color;

            public BannerEntry(string text, Color color)
            {
                Text = text;
                Color = color;
            }
        }

        [Header("UI")]
        public CanvasGroup group;
        public TMP_Text label;

        [Header("Look")]
        public float visibleAlpha = 1f;
        public float hiddenAlpha = 0f;
        public Color friendlyColor = new(0.2f, 0.85f, 0.2f);
        public Color enemyColor = new(0.85f, 0.2f, 0.2f);
        public Color bonusColor = new(0.2f, 0.35f, 0.85f);

        [Header("Display")]
        public float displaySeconds = 2f;
        public bool autoHideWhenEmpty = true;
        [SerializeField] float lingerAfterEmpty = 0.2f;

        [Header("Fade Animation")]
        public bool enableFade = true;
        [Min(0f)] public float fadeInDuration = 0.25f;
        [Min(0f)] public float fadeOutDuration = 0.25f;
        public AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        public AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        readonly Queue<BannerEntry> _queue = new();
        float _timer;
        bool _showing;
        bool _isVisible;
        Coroutine _fadeRoutine;
        float _emptySince = -1f;

        void Awake()
        {
            HideImmediate();
        }

        void Update()
        {
            if (!_showing)
            {
                TryDisplayNext();
                return;
            }

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _showing = false;
                TryDisplayNext();
            }
        }

        /// <summary>
        /// Called by BattleUIService when a new phase begins.
        /// Example: Player phase vs Enemy phase.
        /// </summary>
        public void ShowPhaseBegan(bool isPlayerPhase)
        {
            string who = isPlayerPhase ? "Player" : "Enemy";
            Color color = isPlayerPhase ? friendlyColor : enemyColor;
            EnqueueMessage($"Begin Turn ({who})", color);
        }

        /// <summary>
        /// Called by BattleUIService when a unit actually starts its turn.
        /// </summary>
        public void ShowTurnStarted(Unit unit, bool isPlayerUnit)
        {
            string unitLabel = (unit != null) ? TurnManagerV2.FormatUnitLabel(unit) : (isPlayerUnit ? "Player" : "Enemy");
            Color color = isPlayerUnit ? friendlyColor : enemyColor;
            EnqueueMessage($"Begin {unitLabel}", color);
        }

        /// <summary>
        /// Optional hook for bonus turn style announcements.
        /// </summary>
        public void ShowBonusTurn(string text)
        {
            if (!string.IsNullOrEmpty(text))
                EnqueueMessage(text, bonusColor);
        }

        /// <summary>
        /// Hide instantly, without animation.
        /// </summary>
        public void HideImmediate()
        {
            _queue.Clear();
            _showing = false;
            _timer = 0f;
            _emptySince = -1f;

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            _isVisible = false;

            if (group != null)
            {
                group.alpha = hiddenAlpha;
                group.blocksRaycasts = false;
                group.interactable = false;
            }

            if (label != null)
            {
                label.text = string.Empty;
            }
        }

        void EnqueueMessage(string text, Color color)
        {
            if (string.IsNullOrEmpty(text))
                return;

            _queue.Enqueue(new BannerEntry(text, color));
            if (!_showing)
                TryDisplayNext();
        }

        void TryDisplayNext()
        {
            if (_queue.Count == 0)
            {
                if (_emptySince < 0f)
                    _emptySince = Time.time;

                if (autoHideWhenEmpty && (Time.time - _emptySince) >= lingerAfterEmpty)
                {
                    if (label != null)
                        label.text = string.Empty;

                    SetVisible(false);
                }
                return;
            }

            _emptySince = -1f;

            var entry = _queue.Dequeue();
            if (label != null)
            {
                label.text = entry.Text;
                label.color = entry.Color;
            }

            _timer = Mathf.Max(0.05f, displaySeconds);
            _showing = true;
            SetVisible(true);
        }

        void SetVisible(bool visible)
        {
            if (_isVisible == visible)
            {
                if (!visible && group != null)
                {
                    group.interactable = false;
                    group.blocksRaycasts = false;
                }
                return;
            }

            _isVisible = visible;

            if (group == null)
            {
                if (label != null)
                    label.enabled = visible;
                return;
            }

            if (!enableFade || !isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                group.alpha = visible ? visibleAlpha : hiddenAlpha;
                group.interactable = visible;
                group.blocksRaycasts = visible;
                return;
            }

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            group.interactable = visible;
            group.blocksRaycasts = visible;
            _fadeRoutine = StartCoroutine(FadeCanvas(visible));
        }

        IEnumerator FadeCanvas(bool visible)
        {
            float duration = visible ? fadeInDuration : fadeOutDuration;
            AnimationCurve curve = visible ? fadeInCurve : fadeOutCurve;
            float startAlpha = group != null ? group.alpha : (visible ? hiddenAlpha : visibleAlpha);
            float endAlpha = visible ? visibleAlpha : hiddenAlpha;

            if (group == null)
                yield break;

            if (duration <= 0f)
            {
                group.alpha = endAlpha;
            }
            else
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float curveValue = curve != null ? curve.Evaluate(t) : t;
                    group.alpha = Mathf.LerpUnclamped(startAlpha, endAlpha, curveValue);
                    yield return null;
                }

                group.alpha = endAlpha;
            }

            if (!visible)
            {
                group.interactable = false;
                group.blocksRaycasts = false;
            }

            _fadeRoutine = null;
        }
    }
}
