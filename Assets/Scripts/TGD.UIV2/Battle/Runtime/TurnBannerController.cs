using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TGD.UIV2.Battle
{
    public enum TurnBannerTone
    {
        Friendly,
        Enemy,
        Bonus,
        Neutral,
    }

    /// <summary>
    /// 纯展示用的回合提示 Banner，负责播放淡入淡出动画与颜色。
    /// 它只接受 BattleUIService 分发的消息，不直接耦合 TurnManagerV2。
    /// </summary>
    public sealed class TurnBannerController : MonoBehaviour
    {
        [Header("UI Refs")]
        public TMP_Text messageText;     // 文案，比如 "Begin Turn(Player)"
        public CanvasGroup canvasGroup;  // 整个banner根CanvasGroup，用来淡入淡出
        public Image glow;               // 可选：彩色描边/发光背景框

        [Header("Colors")]
        public Color friendlyColor = new(0.2f, 0.85f, 0.2f); // 友方/玩家绿色
        public Color enemyColor = new(0.85f, 0.2f, 0.2f);    // 敌方红色
        public Color bonusColor = new(0.2f, 0.35f, 0.85f);   // Bonus Turn 蓝色
        public Color neutralColor = new(1f, 1f, 1f);         // 兜底（用于中立/未知）

        [Header("Timing")]
        [Min(0.1f)]
        public float displaySeconds = 2f;          // 保留在屏幕上的时间（不含淡出）
        public bool autoHideWhenEmpty = true;      // 无消息时是否自动隐藏
        [Min(0f)]
        public float lingerAfterEmpty = 0.2f;      // 队列清空后再延迟多久才隐藏

        [Header("Fade Animation")]
        public bool enableFade = true;
        [Min(0f)]
        public float fadeInDuration = 0.25f;
        [Min(0f)]
        public float fadeOutDuration = 0.25f;
        public AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        public AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        readonly Queue<(string message, TurnBannerTone tone)> _queue = new();
        float _timer;
        bool _showing;
        bool _isVisible;
        float _emptySince = -1f;
        Coroutine _fadeRoutine;

        void Awake()
        {
            ForceHideImmediate();
        }

        void OnDisable()
        {
            // rig 关掉时确保瞬间清理干净，不留僵尸 UI
            ForceHideImmediate();
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
        /// BattleUIService 每当发生“需要告诉玩家一条事”的时候就会调这个。
        /// </summary>
        public void EnqueueMessage(string message, TurnBannerTone tone)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            _queue.Enqueue((message, tone));
            if (!_showing)
                TryDisplayNext();
        }

        /// <summary>
        /// BattleUIService 可以在 OnEnable 初始化或 OnDisable 清理的时候叫这个。
        /// 立刻隐藏并清空队列。
        /// </summary>
        public void ForceHideImmediate()
        {
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            _queue.Clear();
            _timer = 0f;
            _showing = false;
            _emptySince = -1f;

            if (messageText != null)
                messageText.text = string.Empty;

            if (canvasGroup != null)
            {
                bool visible = !autoHideWhenEmpty;
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = visible;
                canvasGroup.blocksRaycasts = visible;
            }

            _isVisible = !autoHideWhenEmpty;
        }

        void TryDisplayNext()
        {
            if (_queue.Count == 0)
            {
                if (autoHideWhenEmpty)
                {
                    if (_emptySince < 0f)
                        _emptySince = Time.time;

                    if (Time.time - _emptySince >= lingerAfterEmpty)
                    {
                        SetVisible(false);
                        if (messageText != null)
                            messageText.text = string.Empty;
                    }
                }
                return;
            }

            _emptySince = -1f;
            var entry = _queue.Dequeue();
            Color toneColor = ResolveColor(entry.tone);

            if (messageText != null)
            {
                messageText.text = entry.message;
                messageText.color = toneColor;
            }

            if (glow != null)
                glow.color = toneColor;

            _timer = Mathf.Max(0.1f, displaySeconds);
            _showing = true;
            SetVisible(true);
        }

        Color ResolveColor(TurnBannerTone tone)
        {
            return tone switch
            {
                TurnBannerTone.Enemy => enemyColor,
                TurnBannerTone.Bonus => bonusColor,
                TurnBannerTone.Neutral => neutralColor,
                _ => friendlyColor,
            };
        }

        void SetVisible(bool visible)
        {
            if (_isVisible == visible)
            {
                if (!visible && canvasGroup != null)
                {
                    canvasGroup.interactable = false;
                    canvasGroup.blocksRaycasts = false;
                }
                return;
            }

            _isVisible = visible;

            if (canvasGroup == null)
            {
                if (messageText != null)
                    messageText.enabled = visible;
                return;
            }

            if (!enableFade || !isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = visible;
                canvasGroup.blocksRaycasts = visible;
                return;
            }

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
            _fadeRoutine = StartCoroutine(FadeCanvas(visible));
        }

        IEnumerator FadeCanvas(bool visible)
        {
            if (canvasGroup == null)
            {
                _fadeRoutine = null;
                yield break;
            }

            float duration = visible ? fadeInDuration : fadeOutDuration;
            AnimationCurve curve = visible ? fadeInCurve : fadeOutCurve;
            float startAlpha = canvasGroup.alpha;
            float endAlpha = visible ? 1f : 0f;

            if (duration <= 0f)
            {
                canvasGroup.alpha = endAlpha;
            }
            else
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float curveValue = curve != null ? curve.Evaluate(t) : t;
                    canvasGroup.alpha = Mathf.LerpUnclamped(startAlpha, endAlpha, curveValue);
                    yield return null;
                }

                canvasGroup.alpha = endAlpha;
            }

            if (!visible)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            _fadeRoutine = null;
        }
    }
}
