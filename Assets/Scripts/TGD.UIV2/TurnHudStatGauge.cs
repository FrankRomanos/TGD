using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TGD.UI
{
    /// <summary>
    /// Animates a HUD bar (health/energy) with numeric interpolation, change pulses, and delta readouts.
    /// </summary>
    public sealed class TurnHudStatGauge : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Image fillImage;
        [SerializeField] TMP_Text valueLabel;
        [SerializeField] TMP_Text extraLabel;
        [SerializeField] TMP_Text deltaLabel;
        [SerializeField] RectTransform pulseTarget;

        [Header("Formatting")]
        [SerializeField] string valueFormat = "{0}/{1}";
        [SerializeField] bool clampFill01 = true;
        [SerializeField] bool animateNumbers = true;
        [SerializeField] bool animateMaxValue = false;

        [Header("Value Animation")]
        [SerializeField] float changeDuration = 0.35f;
        [SerializeField] AnimationCurve changeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] bool useUnscaledTime = true;

        [Header("Pulse")]
        [SerializeField] bool pulseOnIncrease = true;
        [SerializeField] bool pulseOnDecrease = true;
        [SerializeField] float increasePulseScale = 1.08f;
        [SerializeField] float decreasePulseScale = 0.94f;
        [SerializeField] float pulseDuration = 0.25f;
        [SerializeField] AnimationCurve pulseCurve = new(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));

        [Header("Delta Text")]
        [SerializeField] bool showDelta = true;
        [SerializeField] float deltaHoldDuration = 1f;
        [SerializeField] float deltaFadeDuration = 0.35f;
        [SerializeField] Color positiveDeltaColor = new(0.35f, 0.95f, 0.55f, 1f);
        [SerializeField] Color negativeDeltaColor = new(0.95f, 0.35f, 0.35f, 1f);

        bool _initialized;
        int _targetCurrent;
        int _targetMax;
        string _targetExtra = string.Empty;

        float _visualCurrent;
        float _visualMax;

        bool _animatingValue;
        float _valueAnimStartTime;
        float _valueAnimDuration;
        float _valueAnimStartCurrent;
        float _valueAnimStartMax;

        bool _pulseActive;
        float _pulseElapsed;
        float _pulseGoalScale = 1f;
        Vector3 _basePulseScale = Vector3.one;

        bool _deltaVisible;
        float _deltaTimer;

        float CurrentTime => useUnscaledTime ? Time.unscaledTime : Time.time;
        float DeltaTime => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        void Awake()
        {
            if (!pulseTarget && fillImage)
                pulseTarget = fillImage.rectTransform;

            if (pulseTarget)
                _basePulseScale = pulseTarget.localScale;

            if (deltaLabel)
                deltaLabel.alpha = 0f;
        }

        void OnEnable()
        {
            if (!_initialized)
                return;

            ApplyVisuals(_targetCurrent, _targetMax);
            UpdateExtraLabel();
            HideDelta();
            ResetPulse();
        }

        void Update()
        {
            UpdateValueAnimation();
            UpdatePulse();
            UpdateDelta();
        }

        /// <summary>
        /// Apply a new stat state to the gauge.
        /// </summary>
        public void SetValue(int current, int max, string extraText = null, bool immediate = false)
        {
            current = Mathf.Max(0, current);
            max = Mathf.Max(0, max);
            string sanitizedExtra = extraText ?? string.Empty;

            if (!_initialized || immediate)
            {
                _initialized = true;
                _targetCurrent = current;
                _targetMax = max;
                _targetExtra = sanitizedExtra;
                _animatingValue = false;
                ApplyVisuals(current, max);
                UpdateExtraLabel();
                HideDelta();
                ResetPulse();
                return;
            }

            bool valuesChanged = current != _targetCurrent || max != _targetMax;
            bool extraChanged = sanitizedExtra != _targetExtra;

            _targetCurrent = current;
            _targetMax = max;
            _targetExtra = sanitizedExtra;

            if (!valuesChanged)
            {
                if (extraChanged)
                    UpdateExtraLabel();
                return;
            }

            float startCurrent = _visualCurrent;
            float startMax = _visualMax;

            StartValueAnimation(startCurrent, startMax);

            int delta = Mathf.RoundToInt(current - startCurrent);
            if (delta != 0)
            {
                TriggerPulse(delta);
                ShowDelta(delta);
            }
            else
            {
                HideDelta();
                if (!_animatingValue)
                    ResetPulse();
            }

            UpdateExtraLabel();
        }

        void StartValueAnimation(float startCurrent, float startMax)
        {
            if (changeDuration <= Mathf.Epsilon)
            {
                _animatingValue = false;
                ApplyVisuals(_targetCurrent, _targetMax);
                return;
            }

            _valueAnimStartTime = CurrentTime;
            _valueAnimDuration = changeDuration;
            _valueAnimStartCurrent = startCurrent;
            _valueAnimStartMax = startMax;
            _animatingValue = true;
        }

        void UpdateValueAnimation()
        {
            if (!_animatingValue)
                return;

            float elapsed = CurrentTime - _valueAnimStartTime;
            float duration = Mathf.Max(0.0001f, _valueAnimDuration);
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = changeCurve != null ? changeCurve.Evaluate(t) : t;

            float currentValue = Mathf.Lerp(_valueAnimStartCurrent, _targetCurrent, eased);
            float maxValue = Mathf.Lerp(_valueAnimStartMax, _targetMax, eased);
            ApplyVisuals(currentValue, maxValue);

            if (t >= 1f - Mathf.Epsilon)
            {
                _animatingValue = false;
                ApplyVisuals(_targetCurrent, _targetMax);
            }
        }

        void ApplyVisuals(float currentValue, float maxValue)
        {
            currentValue = Mathf.Max(0f, currentValue);
            maxValue = Mathf.Max(0f, maxValue);

            _visualCurrent = currentValue;
            _visualMax = maxValue;

            if (fillImage)
            {
                float fill = maxValue > Mathf.Epsilon ? currentValue / maxValue : 0f;
                if (clampFill01)
                    fill = Mathf.Clamp01(fill);
                fillImage.fillAmount = fill;
            }

            if (valueLabel)
            {
                int displayedCurrent = animateNumbers ? Mathf.RoundToInt(currentValue) : _targetCurrent;
                int displayedMax = animateNumbers && animateMaxValue ? Mathf.RoundToInt(maxValue) : _targetMax;
                if (!animateNumbers)
                    displayedCurrent = _targetCurrent;
                if (!animateMaxValue)
                    displayedMax = _targetMax;

                displayedCurrent = Mathf.Max(0, displayedCurrent);
                displayedMax = Mathf.Max(0, displayedMax);

                if (string.IsNullOrEmpty(valueFormat))
                    valueLabel.text = $"{displayedCurrent}/{displayedMax}";
                else
                    valueLabel.text = string.Format(valueFormat, displayedCurrent, displayedMax);
            }
        }

        void UpdateExtraLabel()
        {
            if (!extraLabel)
                return;

            extraLabel.text = _targetExtra;
        }

        void TriggerPulse(int delta)
        {
            if (!pulseTarget)
            {
                if (fillImage)
                    pulseTarget = fillImage.rectTransform;
                if (!pulseTarget)
                    return;
            }

            if ((delta > 0 && !pulseOnIncrease) || (delta < 0 && !pulseOnDecrease))
                return;

            float targetScale = delta >= 0 ? increasePulseScale : decreasePulseScale;
            if (Mathf.Approximately(targetScale, 1f))
            {
                ResetPulse();
                return;
            }

            _pulseActive = true;
            _pulseElapsed = 0f;
            _pulseGoalScale = targetScale;
        }

        void UpdatePulse()
        {
            if (!_pulseActive || !pulseTarget)
                return;

            _pulseElapsed += DeltaTime;
            float duration = Mathf.Max(0.0001f, pulseDuration);
            float t = Mathf.Clamp01(_pulseElapsed / duration);
            float factor = pulseCurve != null ? pulseCurve.Evaluate(t) : t;
            float scale = Mathf.LerpUnclamped(1f, _pulseGoalScale, factor);
            pulseTarget.localScale = _basePulseScale * scale;

            if (t >= 1f - Mathf.Epsilon)
            {
                _pulseActive = false;
                ResetPulse();
            }
        }

        void ResetPulse()
        {
            if (!pulseTarget)
                return;

            pulseTarget.localScale = _basePulseScale;
        }

        void ShowDelta(int delta)
        {
            if (!showDelta || !deltaLabel)
                return;

            _deltaVisible = true;
            _deltaTimer = 0f;
            deltaLabel.alpha = 1f;
            deltaLabel.text = delta > 0 ? $"+{delta}" : delta.ToString();
            deltaLabel.color = delta > 0 ? positiveDeltaColor : negativeDeltaColor;
        }

        void HideDelta()
        {
            if (!deltaLabel)
                return;

            _deltaVisible = false;
            _deltaTimer = 0f;
            deltaLabel.alpha = 0f;
        }

        void UpdateDelta()
        {
            if (!_deltaVisible || !deltaLabel)
                return;

            _deltaTimer += DeltaTime;
            if (_deltaTimer <= deltaHoldDuration)
                return;

            float fadeT = (_deltaTimer - deltaHoldDuration) / Mathf.Max(0.0001f, deltaFadeDuration);
            if (fadeT >= 1f)
            {
                HideDelta();
                return;
            }

            deltaLabel.alpha = Mathf.Lerp(1f, 0f, fadeT);
        }
    }
}
