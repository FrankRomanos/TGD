using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TGD.UIV2
{
    /// <summary>
    /// Handles the hourglass presentation for the Turn HUD: idle sway, consume bounce and refund flash.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TurnHudHourglass : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] Image targetImage;
        [SerializeField] RectTransform targetRect;

        [Header("Idle Sway")]
        [Tooltip("Degrees of rotation applied while the hourglass is available.")]
        public float swayAmplitude = 4f;
        [Tooltip("Oscillations per second for the idle sway motion.")]
        public float swayFrequency = 2.25f;
        [Tooltip("Random phase offset added per instance to avoid synchronized sway.")]
        public float swayPhaseJitter = 0.35f;
        [Tooltip("When enabled, the animation ignores time scale changes.")]
        public bool useUnscaledTime = true;

        [Header("Consume Animation")]
        [Tooltip("Minimum scale applied when a second is consumed before bouncing back to 1.0.")]
        public float consumeBounceScale = 0.85f;
        [Tooltip("Duration of the consume bounce animation in seconds.")]
        public float consumeBounceDuration = 0.3f;
        [Tooltip("Highlight color used briefly when a second is consumed.")]
        public Color consumeFlashColor = Color.white;
        [Tooltip("Scale curve used during the consume bounce (0..1 time).")]
        public AnimationCurve consumeBounceCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Intensity curve for the consume flash (0..1 time, value lerps to the base color).")]
        public AnimationCurve consumeFlashCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Header("Refund Animation")]
        [Tooltip("Maximum scale applied when a second is refunded before returning to 1.0.")]
        public float refundBounceScale = 1.15f;
        [Tooltip("Duration of the refund bounce animation in seconds.")]
        public float refundBounceDuration = 0.35f;
        [Tooltip("Highlight color used briefly when a second becomes available again.")]
        public Color refundFlashColor = new(1f, 0.95f, 0.6f, 1f);
        [Tooltip("Scale curve used during the refund bounce (0..1 time).")]
        public AnimationCurve refundBounceCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Intensity curve for the refund flash (0..1 time, value lerps to the base color).")]
        public AnimationCurve refundFlashCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        Sprite _availableSprite;
        Sprite _consumedSprite;
        Color _availableColor = Color.white;
        Color _consumedColor = Color.gray;
        bool _consumed;
        float _phaseOffset;
        float _phaseJitter;
        Coroutine _activeRoutine;
        bool _rotationNeedsReset;

        Image TargetImage => targetImage ? targetImage : (targetImage = GetComponent<Image>());
        RectTransform TargetRect => targetRect ? targetRect : (targetRect = GetComponent<RectTransform>());

        void Awake()
        {
            if (!targetImage)
                targetImage = GetComponent<Image>();
            if (!targetRect)
                targetRect = GetComponent<RectTransform>();

            if (targetImage)
                targetImage.raycastTarget = false;

            _phaseOffset = Random.Range(0f, Mathf.PI * 2f);
            _phaseJitter = Random.Range(-swayPhaseJitter, swayPhaseJitter);
        }

        void OnEnable()
        {
            ApplyVisualState();
        }

        void OnDisable()
        {
            StopActiveRoutine();
            ResetVisualTransform(true);
        }

        void Update()
        {
            var rect = TargetRect;
            if (!rect)
                return;

            if (!_consumed && swayAmplitude > 0f && swayFrequency > 0f)
            {
                float time = useUnscaledTime ? Time.unscaledTime : Time.time;
                float phase = time * swayFrequency * Mathf.PI * 2f + _phaseOffset + _phaseJitter;
                float angle = Mathf.Sin(phase) * swayAmplitude;
                rect.localRotation = Quaternion.Euler(0f, 0f, angle);
                _rotationNeedsReset = true;
            }
            else if (_rotationNeedsReset)
            {
                rect.localRotation = Quaternion.identity;
                _rotationNeedsReset = false;
            }
        }

        public void ConfigureSprites(Sprite available, Sprite consumed, Color availableColor, Color consumedColor)
        {
            _availableSprite = available;
            _consumedSprite = consumed ? consumed : available;
            _availableColor = availableColor;
            _consumedColor = consumedColor;
            ApplyVisualState();
        }

        public void SetConsumed(bool consumed, bool animate)
        {
            if (_consumed == consumed)
            {
                if (!animate)
                    ApplyVisualState();
                return;
            }

            _consumed = consumed;

            if (!isActiveAndEnabled)
            {
                StopActiveRoutine();
                ApplyVisualState();
                return;
            }

            StopActiveRoutine();

            if (animate)
            {
                _activeRoutine = StartCoroutine(consumed ? PlayConsumeSequence() : PlayRefundSequence());
            }
            else
            {
                ApplyVisualState();
            }
        }

        void ApplyVisualState()
        {
            var image = TargetImage;
            if (!image)
                return;

            if (_consumed)
            {
                if (_consumedSprite)
                    image.sprite = _consumedSprite;
                else if (_availableSprite)
                    image.sprite = _availableSprite;
                image.color = _consumedColor;
                ResetVisualTransform(true);
            }
            else
            {
                if (_availableSprite)
                    image.sprite = _availableSprite;
                image.color = _availableColor;
                ResetVisualTransform(false);
                _rotationNeedsReset = true;
            }
        }

        IEnumerator PlayConsumeSequence()
        {
            var rect = TargetRect;
            var image = TargetImage;
            if (!rect || !image)
            {
                ApplyVisualState();
                yield break;
            }

            ResetVisualTransform(true);

            float duration = Mathf.Max(0.05f, consumeBounceDuration);
            float elapsed = 0f;
            float startScale = Mathf.Max(0.1f, consumeBounceScale);

            if (_consumedSprite)
                image.sprite = _consumedSprite;
            else if (_availableSprite)
                image.sprite = _availableSprite;

            while (elapsed < duration)
            {
                elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float bounceT = consumeBounceCurve != null ? consumeBounceCurve.Evaluate(t) : t;
                float scale = Mathf.LerpUnclamped(startScale, 1f, bounceT);
                rect.localScale = Vector3.one * scale;

                float flashT = consumeFlashCurve != null ? consumeFlashCurve.Evaluate(t) : (1f - t);
                image.color = Color.LerpUnclamped(_consumedColor, consumeFlashColor, flashT);
                yield return null;
            }

            rect.localScale = Vector3.one;
            image.color = _consumedColor;
            ResetVisualTransform(true);
            _activeRoutine = null;
        }

        IEnumerator PlayRefundSequence()
        {
            var rect = TargetRect;
            var image = TargetImage;
            if (!rect || !image)
            {
                ApplyVisualState();
                yield break;
            }

            ResetVisualTransform(true);

            float duration = Mathf.Max(0.05f, refundBounceDuration);
            float elapsed = 0f;
            float startScale = Mathf.Max(1f, refundBounceScale);

            if (_availableSprite)
                image.sprite = _availableSprite;

            while (elapsed < duration)
            {
                elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float bounceT = refundBounceCurve != null ? refundBounceCurve.Evaluate(t) : t;
                float scale = Mathf.LerpUnclamped(startScale, 1f, bounceT);
                rect.localScale = Vector3.one * scale;

                float flashT = refundFlashCurve != null ? refundFlashCurve.Evaluate(t) : (1f - t);
                image.color = Color.LerpUnclamped(_availableColor, refundFlashColor, flashT);
                yield return null;
            }

            rect.localScale = Vector3.one;
            image.color = _availableColor;
            _rotationNeedsReset = true;
            _activeRoutine = null;
        }

        void ResetVisualTransform(bool resetRotation)
        {
            var rect = TargetRect;
            if (!rect)
                return;

            rect.localScale = Vector3.one;
            if (resetRotation)
            {
                rect.localRotation = Quaternion.identity;
                _rotationNeedsReset = false;
            }
        }

        void StopActiveRoutine()
        {
            if (_activeRoutine != null)
            {
                StopCoroutine(_activeRoutine);
                _activeRoutine = null;
            }
        }
    }
}
