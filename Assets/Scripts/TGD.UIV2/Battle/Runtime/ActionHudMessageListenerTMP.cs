using System.Collections;
using TMPro;
using UnityEngine;

namespace TGD.UIV2
{
    /// <summary>
    /// Pure visual controller for the action HUD message display. Handles gradients,
    /// shakes, pulses and timed visibility but delegates all gameplay events to
    /// <see cref="Battle.BattleUIService"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActionHudMessageListenerTMP : MonoBehaviour
    {
        [Header("Targets")]
        public TMP_Text uiText;
        public RectTransform root;

        [Header("Debug / Preview")]
        public bool forceKind = false;
        public HudKind forcedKind = HudKind.Energy; // 运行时可在 Inspector 切换

        [Header("Behaviour")]
        [Min(0.2f)] public float showSeconds = 1.6f;
        public bool fadeOut = true;

        [Header("Effects - Shake")]
        public bool enableShake = true;
        [Range(0f, 30f)] public float shakeAmplitude = 6f;   // 像素振幅
        [Range(0f, 60f)] public float shakeFrequency = 18f;  // Hz

        [Header("Effects - Pulse (scale)")]
        public bool enablePulse = true;
        [Range(0f, 0.25f)] public float pulseAmplitude = 0.06f; // ±6%
        [Range(0.1f, 20f)] public float pulseFrequency = 6f;    // 次/秒

        [Header("Effects - Gradient Colors (TMP VertexGradient)")]
        public HudGradient gradientInfo = HudGradient.InfoDefault();
        public HudGradient gradientEnergy = HudGradient.EnergyDefault();
        public HudGradient gradientTime = HudGradient.TimeDefault();
        public HudGradient gradientSnare = HudGradient.SnareDefault();

        public bool IsVisible => _isVisible;
        public string CurrentMessage { get; private set; } = string.Empty;

        CanvasGroup _canvasGroup;
        Coroutine _co;
        Vector2 _baseAnchoredPosition;
        Vector3 _baseScale = Vector3.one;
        bool _isVisible;

        void Reset()
        {
            if (!uiText) uiText = GetComponentInChildren<TMP_Text>(true);
            if (!root && uiText) root = uiText.rectTransform;
        }

        void Awake()
        {
            if (!uiText) uiText = GetComponentInChildren<TMP_Text>(true);
            if (!root && uiText) root = uiText.rectTransform;
            if (root)
            {
                _canvasGroup = root.GetComponent<CanvasGroup>() ?? root.gameObject.AddComponent<CanvasGroup>();
                _baseAnchoredPosition = root.anchoredPosition;
                _baseScale = root.localScale;
            }
            SetVisible(false, true);
        }

        void OnDisable()
        {
            if (_co != null)
            {
                StopCoroutine(_co);
                _co = null;
            }

            ResetTransforms();
            SetVisible(false);
            _isVisible = false;
            CurrentMessage = string.Empty;
        }

        /// <summary>
        /// Immediately hides the HUD and cancels active animations.
        /// </summary>
        public void HideImmediate()
        {
            if (_co != null)
            {
                StopCoroutine(_co);
                _co = null;
            }

            ResetTransforms();
            SetVisible(false, true);
            _isVisible = false;
            CurrentMessage = string.Empty;
        }

        /// <summary>
        /// Displays a message using the configured gradients and animation style.
        /// </summary>
        public void ShowMessage(string text, HudKind kind)
        {
            if (!uiText || !root)
                return;

            if (forceKind)
                kind = forcedKind;   // 允许在 Inspector 强制预览配色

            ApplyGradient(kind);                 // 强制写入渐变
            uiText.text = text;
            CurrentMessage = text ?? string.Empty;

            if (_co != null)
                StopCoroutine(_co);

            _co = StartCoroutine(ShowThenHide());
        }

        void ResetTransforms()
        {
            if (!root)
                return;

            root.anchoredPosition = _baseAnchoredPosition;
            root.localScale = _baseScale;
            if (_canvasGroup)
                _canvasGroup.alpha = 1f;
        }

        void ApplyGradient(HudKind kind)
        {
            if (!uiText)
                return;

            // —— 统一基础状态，避免被默认颜色/预设覆盖 ——
            uiText.richText = true;
            uiText.overrideColorTags = false;
            uiText.color = Color.white;                 // 顶点基色设白
            uiText.enableVertexGradient = true;
#if TMP_PRESENT
            uiText.colorGradientPreset = null;          // 不用外部 Gradient 资产
#endif

            // 强制把“当前实例材质”的面色设为白（避免材质预设染色）
            var mat = uiText.fontMaterial;              // 注意：这是实例，不是共享
            if (mat && mat.HasProperty(TMPro.ShaderUtilities.ID_FaceColor))
                mat.SetColor(TMPro.ShaderUtilities.ID_FaceColor, Color.white);

            // —— 选我们自己的四角渐变 ——
            var gradient = kind switch
            {
                HudKind.Energy => gradientEnergy,
                HudKind.Time => gradientTime,
                HudKind.Snare => gradientSnare,
                _ => gradientInfo
            };
            uiText.colorGradient = gradient.ToVertexGradient();

            // 刷新网格
            uiText.SetVerticesDirty();
            uiText.SetLayoutDirty();
        }

        IEnumerator ShowThenHide()
        {
            SetVisible(true, true);

            if (root)
            {
                _baseAnchoredPosition = root.anchoredPosition;
                _baseScale = root.localScale;
            }

            float elapsed = 0f;

            while (elapsed < showSeconds)
            {
                elapsed += Time.unscaledDeltaTime;

                if (root)
                {
                    if (enableShake)
                    {
                        float nx = (Mathf.PerlinNoise(31f, elapsed * shakeFrequency) - 0.5f) * 2f;
                        float ny = (Mathf.PerlinNoise(73f, elapsed * shakeFrequency) - 0.5f) * 2f;
                        root.anchoredPosition = _baseAnchoredPosition + new Vector2(nx, ny) * shakeAmplitude;
                    }

                    if (enablePulse)
                    {
                        float s = 1f + Mathf.Sin(elapsed * Mathf.PI * 2f * pulseFrequency) * pulseAmplitude;
                        root.localScale = new Vector3(s, s, 1f);
                    }
                }

                yield return null;
            }

            ResetTransforms();

            if (!fadeOut)
            {
                SetVisible(false);
                _co = null;
                yield break;
            }

            if (_canvasGroup)
            {
                float duration = 0.25f;
                float d = 0f;
                while (d < duration)
                {
                    d += Time.unscaledDeltaTime;
                    _canvasGroup.alpha = Mathf.Lerp(1f, 0f, d / duration);
                    yield return null;
                }
            }

            SetVisible(false);
            _co = null;
        }

        void SetVisible(bool visible, bool resetAlpha = false)
        {
            if (!root)
                return;

            if (resetAlpha && _canvasGroup)
                _canvasGroup.alpha = 1f;

            root.gameObject.SetActive(visible);
            _isVisible = visible;
            if (!visible)
                CurrentMessage = string.Empty;
        }

        // ---------- gradient helper ----------
        [System.Serializable]
        public struct HudGradient
        {
            public Color topLeft, topRight, bottomLeft, bottomRight;

            public VertexGradient ToVertexGradient()
                => new(topLeft, topRight, bottomLeft, bottomRight);

            public static HudGradient InfoDefault()
                => FromHex("#E6FFFA", "#C9FFF3", "#81D8D0", "#17CFC0");
            public static HudGradient EnergyDefault() => FromHex("#FFD35A", "#FF7A1A", "#FFB000", "#FF5A00");
            public static HudGradient TimeDefault() => FromHex("#67E8F9", "#60A5FA", "#00D1FF", "#0077FF");
            public static HudGradient SnareDefault() => FromHex("#E879F9", "#C084FC", "#F472B6", "#A78BFA");

            public static HudGradient FromHex(string tl, string tr, string bl, string br)
            {
                ColorUtility.TryParseHtmlString(tl, out var ctl);
                ColorUtility.TryParseHtmlString(tr, out var ctr);
                ColorUtility.TryParseHtmlString(bl, out var cbl);
                ColorUtility.TryParseHtmlString(br, out var cbr);
                return new HudGradient
                {
                    topLeft = ctl,
                    topRight = ctr,
                    bottomLeft = cbl,
                    bottomRight = cbr
                };
            }
        }

        public enum HudKind
        {
            Info,
            Energy,
            Time,
            Snare
        }
    }
}
