using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

namespace TGD.UIV2.Battle
{
    /// <summary>
    /// 纯展示用的回合提示 Banner。
    /// 它自己不订阅 TurnManagerV2，不知道谁是玩家谁是敌人。
    /// BattleUIService 是唯一能调用它的“指挥官”。
    /// </summary>
    public sealed class TurnBannerController : MonoBehaviour
    {
        [Header("UI Refs")]
        public TMP_Text messageText;     // 文案，比如 "玩家回合开始" / "某某 的回合开始"
        public CanvasGroup canvasGroup;  // 整个banner根CanvasGroup，用来淡入淡出
        public Image glow;               // 可选：你那个彩色描边/发光背景框，没有就留空

        [Header("Colors")]
        public Color friendlyColor = new(0.2f, 0.85f, 0.2f); // 友方/玩家绿色
        public Color enemyColor = new(0.85f, 0.2f, 0.2f); // 敌方红色
        public Color neutralColor = new(1f, 1f, 1f);    // 兜底（如果我们以后想播中立提示）

        [Header("Timing")]
        [Min(0.1f)]
        public float displaySeconds = 1.2f; // 保留在屏幕上的时间（不含淡出）
        [Min(0f)]
        public float fadeOutDuration = 0.25f; // 淡出多久
        public bool enableFadeOut = true;
        public AnimationCurve fadeOutCurve =
            AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        float _timer;
        bool _showing;
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
                return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _showing = false;
                BeginFadeOut();
            }
        }

        /// <summary>
        /// BattleUIService 每当发生“需要告诉玩家一条事”的时候就会调这个。
        /// isPlayerSide = true 用友方配色，false 用敌方配色。
        /// 你可以传中文、原先的 "Begin T1(1P)" 风格、随便。
        /// </summary>
        public void ShowBanner(string message, bool isPlayerSide)
        {
            if (messageText != null)
            {
                messageText.text = message ?? string.Empty;
                messageText.color = isPlayerSide ? friendlyColor : enemyColor;
            }

            if (glow != null)
            {
                glow.color = isPlayerSide ? friendlyColor : enemyColor;
            }

            // 立刻亮出来
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            _timer = Mathf.Max(0.1f, displaySeconds);
            _showing = true;
        }

        /// <summary>
        /// BattleUIService 可以在 OnEnable 初始化或 OnDisable 清理的时候叫这个。
        /// 立刻隐藏，不留文字，不留alpha。
        /// </summary>
        public void ForceHideImmediate()
        {
            _showing = false;
            _timer = 0f;

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            if (messageText != null)
                messageText.text = string.Empty;
        }

        void BeginFadeOut()
        {
            if (!enableFadeOut || canvasGroup == null || !isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                ForceHideImmediate();
                return;
            }

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            _fadeRoutine = StartCoroutine(FadeOutRoutine());
        }

        IEnumerator FadeOutRoutine()
        {
            float dur = Mathf.Max(0f, fadeOutDuration);
            float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;
            float endAlpha = 0f;

            if (dur <= 0f)
            {
                if (canvasGroup != null)
                    canvasGroup.alpha = 0f;
            }
            else
            {
                float t = 0f;
                while (t < dur)
                {
                    t += Time.deltaTime;
                    float u = dur <= 0f ? 1f : Mathf.Clamp01(t / dur);
                    float k = fadeOutCurve != null ? fadeOutCurve.Evaluate(u) : u;
                    if (canvasGroup != null)
                        canvasGroup.alpha = Mathf.LerpUnclamped(startAlpha, endAlpha, k);
                    yield return null;
                }
                if (canvasGroup != null)
                    canvasGroup.alpha = endAlpha;
            }

            if (canvasGroup != null)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            _fadeRoutine = null;
        }
    }
}
