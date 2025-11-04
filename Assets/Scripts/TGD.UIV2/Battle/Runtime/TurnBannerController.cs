using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Febucci.UI;        // TextAnimator_TMP, TypewriterByCharacter
using Febucci.UI.Core;  // TypewriterCore

namespace TGD.UIV2.Battle
{
    public enum TurnBannerTone { Friendly, Enemy, Bonus, Neutral }

    public sealed class TurnBannerController : MonoBehaviour
    {
        [Header("UI Refs")]
        public TMP_Text messageText;
        public CanvasGroup canvasGroup;
        public Image glow;

        [Header("Colors")]
        public Color friendlyColor = new(0.2f, 0.85f, 0.2f);
        public Color enemyColor = new(0.85f, 0.2f, 0.2f);
        public Color bonusColor = new(0.2f, 0.35f, 0.85f);
        public Color neutralColor = new(1f, 1f, 1f);

        [Header("Timing")]
        [Min(0.1f)] public float displaySeconds = 2f;
        public bool autoHideWhenEmpty = true;
        [Min(0f)] public float lingerAfterEmpty = 0.2f;

        [Header("Time Source")]
        public bool useUnscaledTime = true; // <— 新增：UI 计时不受 timeScale 影响

        [Header("Fade Animation")]
        public bool enableFade = true;
        [Min(0f)] public float fadeInDuration = 0.25f;
        [Min(0f)] public float fadeOutDuration = 0.25f;
        public AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

        [Header("Typewriter (Text Animator)")]
        public bool useTypewriter = true;
        public bool autoAddTypewriterIfMissing = true;
        [SerializeField] TextAnimator_TMP textAnimator;
        [SerializeField] TypewriterCore typewriter;
        [Min(5f)] public float charsPerSecondEstimate = 45f;
        [Min(0f)] public float extraHoldAfterReveal = 0.35f;
        public bool skipWhenQueueingNext = true;

        // 放在 Typewriter 区域下面即可
        [Header("Typewriter (Built-in TMP)")]
        public bool useBuiltinTypewriter = true;       // 开关：用 TMP 的逐字显示
        [Min(1f)] public float charsPerSecond = 18f;   // 每秒字符数（先低一点好观察）
        [Min(0f)] public float holdAfterReveal = 0.35f;// 全部出现后额外停留
        Coroutine _typingRoutine;                      // 运行时协程句柄

        [Header("Queue Policy")]
        public bool replaceQueuedWithLatest = false;   // <— 新增：只保留最新
        public bool collapseRepeats = true;            // <— 新增：去重限频
        [Min(0f)] public float minGapSameMessage = 0.75f;

        readonly Queue<(string message, TurnBannerTone tone)> _queue = new();
        float _timer;
        bool _showing;
        bool _isVisible;
        float _emptySince = -1f;
        Coroutine _fadeRoutine;

        // 限频缓存
        string _lastMsg;
        float _lastMsgTime;

        float DT => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float NOW => useUnscaledTime ? Time.unscaledTime : Time.time;

        void Awake() => ForceHideImmediate();

        void OnDisable()
        {
            ForceHideImmediate();
            typewriter?.SkipTypewriter();
        }

        void Update()
        {
            if (!_showing)
            {
                TryDisplayNext();
                return;
            }

            _timer -= DT;
            if (_timer <= 0f)
            {
                _showing = false;
                TryDisplayNext();
            }
        }

        // ===== Public API =====
        void CancelTyping()
        {
            if (_typingRoutine != null)
            {
                StopCoroutine(_typingRoutine);
                _typingRoutine = null;
            }
            if (messageText) messageText.maxVisibleCharacters = int.MaxValue; // 立刻显示全字
        }
        public void EnqueueMessage(string message, TurnBannerTone tone)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            // 去重限频：同一条在 minGap 内丢弃
            if (collapseRepeats && message == _lastMsg && (NOW - _lastMsgTime) < minGapSameMessage)
                return;

            _lastMsg = message;
            _lastMsgTime = NOW;

            if (replaceQueuedWithLatest)
            {
                _queue.Clear();
            }

            _queue.Enqueue((message, tone));
            if (!_showing) TryDisplayNext();
        }

        public void ForceHideImmediate()
        {
            if (_fadeRoutine != null) { StopCoroutine(_fadeRoutine); _fadeRoutine = null; }
            _queue.Clear();
            _timer = 0f;
            _showing = false;
            _emptySince = -1f;
            CancelTyping(); // <— 新增：不留未完成的打字
            typewriter?.SkipTypewriter();
            if (messageText) messageText.text = string.Empty;

            if (canvasGroup)
            {
                bool visible = !autoHideWhenEmpty;
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = visible;
                canvasGroup.blocksRaycasts = visible;
            }
            _isVisible = !autoHideWhenEmpty;
        }

        // ===== Internal =====

        void TryDisplayNext()
        {
            if (_queue.Count == 0)
            {
                if (autoHideWhenEmpty)
                {
                    if (_emptySince < 0f) _emptySince = NOW;
                    if (NOW - _emptySince >= lingerAfterEmpty)
                    {
                        SetVisible(false);
                        if (messageText) messageText.text = string.Empty;
                    }
                }
                return;
            }

            _emptySince = -1f;
            var (msg, tone) = _queue.Dequeue();
            var toneColor = ResolveColor(tone);

            if (messageText)
            {
                messageText.color = toneColor; // 不改颜色体系
                messageText.richText = true;
            }
            if (glow) glow.color = toneColor;

            if (useBuiltinTypewriter && messageText)
            {
                // 替代 Text Animator：用 TMP 的逐字显示
                CancelTyping();

                messageText.text = msg;          // 不改颜色
                messageText.richText = true;     // 允许富文本
                messageText.ForceMeshUpdate();   // 立刻生成字符信息

                int total = messageText.textInfo.characterCount; // 只统计可见字符
                messageText.maxVisibleCharacters = 0;

                // 计算总展示时长：打字时间 + 额外停留，和固定 displaySeconds 取较大
                float reveal = total / Mathf.Max(1f, charsPerSecond);
                _timer = Mathf.Max(displaySeconds, reveal + holdAfterReveal);

                _typingRoutine = StartCoroutine(RevealCharsTMP(total));
            }
            else
            {
                // 直接整段显示
                if (messageText) messageText.text = msg;
                _timer = Mathf.Max(0.1f, displaySeconds);
            }

            _showing = true;
            SetVisible(true);
        }
        IEnumerator RevealCharsTMP(int total)
        {
            float cps = Mathf.Max(1f, charsPerSecond);
            int shown = 0;
            float acc = 0f;

            while (shown < total)
            {
                acc += (useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime) * cps;

                int target = Mathf.Min(total, Mathf.FloorToInt(acc));
                if (target != shown)
                {
                    shown = target;
                    messageText.maxVisibleCharacters = shown;
                    messageText.ForceMeshUpdate();
                }
                yield return null;
            }

            _typingRoutine = null; // 打字结束，剩下交给 _timer 倒计时/淡出
        }
        void TryEnsureTypewriter()
        {
            if (!messageText) return;

            if (!textAnimator && autoAddTypewriterIfMissing)
                textAnimator = messageText.GetComponent<TextAnimator_TMP>()
                            ?? messageText.gameObject.AddComponent<TextAnimator_TMP>();

            if (!typewriter && autoAddTypewriterIfMissing)
            {
                typewriter = messageText.GetComponent<TypewriterCore>();
                if (!typewriter)
                {
                    var tbc = messageText.GetComponent<TypewriterByCharacter>()
                           ?? messageText.gameObject.AddComponent<TypewriterByCharacter>();
                    typewriter = tbc;
                }
            }

            if (textAnimator) textAnimator.enabled = true;
            if (typewriter) ((Behaviour)typewriter).enabled = true;
        }

        Color ResolveColor(TurnBannerTone tone) => tone switch
        {
            TurnBannerTone.Enemy => enemyColor,
            TurnBannerTone.Bonus => bonusColor,
            TurnBannerTone.Neutral => neutralColor,
            _ => friendlyColor,
        };

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
                if (messageText) messageText.enabled = visible;
                return;
            }

            if (!enableFade || !isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = visible;
                canvasGroup.blocksRaycasts = visible;
                return;
            }

            if (_fadeRoutine != null) { StopCoroutine(_fadeRoutine); _fadeRoutine = null; }
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
            _fadeRoutine = StartCoroutine(FadeCanvas(visible));
        }

        IEnumerator FadeCanvas(bool visible)
        {
            if (!canvasGroup) { _fadeRoutine = null; yield break; }

            float duration = visible ? fadeInDuration : fadeOutDuration;
            AnimationCurve curve = visible ? fadeInCurve : fadeOutCurve;
            float startAlpha = canvasGroup.alpha;
            float endAlpha = visible ? 1f : 0f;

            if (duration <= 0f) canvasGroup.alpha = endAlpha;
            else
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += DT; // <— 改用 unscaled/Scaled 统一入口
                    float t = Mathf.Clamp01(elapsed / duration);
                    float v = curve != null ? curve.Evaluate(t) : t;
                    canvasGroup.alpha = Mathf.LerpUnclamped(startAlpha, endAlpha, v);
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
