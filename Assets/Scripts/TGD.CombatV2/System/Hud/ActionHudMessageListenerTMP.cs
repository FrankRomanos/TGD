using System.Collections;
using TMPro;
using TGD.CoreV2;
using TGD.HexBoard;
using UnityEngine;
using Unit = TGD.CoreV2.Unit;

namespace TGD.CombatV2
{
    /// <summary>
    /// Consolidated HUD listener for move/attack rejections and refunds.
    /// 耦合版：内置“轻微摇晃 + 轻微缩放脉冲 + 彩色渐变（强制覆盖）”
    /// 不依赖 UIV2 / 第三方插件。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActionHudMessageListenerTMP : MonoBehaviour, IBindContext
    {
        [Header("Targets")]
        public TMP_Text uiText;
        public RectTransform root;

        [Header("Debug / Preview")]
        public bool forceKind = false;
        public HudKind forcedKind = HudKind.Energy; // 运行时可在 Inspector 切换

        [Header("Filter")]
        [SerializeField] UnitRuntimeContext ctx;
        public bool requireUnitMatch = true;

        [Header("Sources")]
        public bool listenMove = true;
        public bool listenAttack = true;

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

        CanvasGroup _canvasGroup;
        Coroutine _co;

        void Reset()
        {
            if (!uiText) uiText = GetComponentInChildren<TMP_Text>(true);
            if (!root && uiText) root = uiText.rectTransform;
            if (!ctx) ctx = GetComponentInParent<UnitRuntimeContext>(true);
        }

        void Awake()
        {
            if (!uiText) uiText = GetComponentInChildren<TMP_Text>(true);
            if (!root && uiText) root = uiText.rectTransform;
            if (root) _canvasGroup = root.GetComponent<CanvasGroup>() ?? root.gameObject.AddComponent<CanvasGroup>();
            if (!ctx) ctx = GetComponentInParent<UnitRuntimeContext>(true);
            SetVisible(false, true);
        }

        void OnEnable()
        {
            if (listenMove)
            {
                HexMoveEvents.MoveRejected += OnMoveRejected;
                HexMoveEvents.TimeRefunded += OnMoveRefunded;
            }

            if (listenAttack)
            {
                AttackEventsV2.AttackRejected += OnAttackRejected;
                AttackEventsV2.AttackMiss += OnAttackMiss;
            }
        }

        void OnDisable()
        {
            if (listenMove)
            {
                HexMoveEvents.MoveRejected -= OnMoveRejected;
                HexMoveEvents.TimeRefunded -= OnMoveRefunded;
            }

            if (listenAttack)
            {
                AttackEventsV2.AttackRejected -= OnAttackRejected;
                AttackEventsV2.AttackMiss -= OnAttackMiss;
            }

            if (_co != null)
            {
                StopCoroutine(_co);
                _co = null;
            }

            SetVisible(false);
            if (root) root.localScale = Vector3.one;
        }

        public UnitRuntimeContext Context => ctx;
        public Unit OwnerUnit => ctx != null ? ctx.boundUnit : null;

        public void BindContext(UnitRuntimeContext context, TurnManagerV2 _)
        {
            ctx = context;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!ctx)
                ctx = GetComponentInParent<UnitRuntimeContext>(true);
        }
#endif

        Unit UnitRef => OwnerUnit;
        bool Matches(Unit u) => !requireUnitMatch || (u != null && u == UnitRef);

        // ---------- events ----------
        void OnMoveRefunded(Unit unit, int seconds)
        {
            if (!listenMove || !Matches(unit) || !uiText || !root) return;
            Show($"+{seconds}s refunded", HudKind.Time);
        }

        void OnMoveRejected(Unit unit, MoveBlockReason reason, string message)
        {
            if (!listenMove || !Matches(unit) || !uiText || !root) return;

            if (string.IsNullOrEmpty(message))
            {
                message = reason switch
                {
                    MoveBlockReason.Entangled => "I can't move!",
                    MoveBlockReason.NoSteps => "Not now!",
                    MoveBlockReason.OnCooldown => "Move is on cooldown.",
                    MoveBlockReason.NotEnoughResource => "Not enough energy.",
                    MoveBlockReason.PathBlocked => "That path is blocked.",
                    MoveBlockReason.NoBudget => "No More Time",
                    _ => "Can't move."
                };
            }

            Show(message, MapKindForMove(reason, message));
        }

        void OnAttackRejected(Unit unit, AttackRejectReasonV2 reason, string message)
        {
            if (!listenAttack || !Matches(unit) || !uiText || !root) return;

            if (string.IsNullOrEmpty(message))
            {
                message = reason switch
                {
                    AttackRejectReasonV2.NotReady => "Attack not ready.",
                    AttackRejectReasonV2.Busy => "Already attacking.",
                    AttackRejectReasonV2.OnCooldown => "Attack is on cooldown.",
                    AttackRejectReasonV2.NotEnoughResource => "Not enough energy.",
                    AttackRejectReasonV2.NoPath => "Can't reach that target.",
                    AttackRejectReasonV2.CantMove => "Can't move to attack.",
                    _ => "Attack unavailable."
                };
            }

            Show(message, MapKindForAttack(reason, message));
        }

        void OnAttackMiss(Unit unit, string message)
        {
            if (!listenAttack || !Matches(unit) || !uiText || !root) return;
            Show(string.IsNullOrEmpty(message) ? "Attack missed." : message, HudKind.Info);
        }

        // ---------- show / effects ----------
        public enum HudKind { Info, Energy, Time, Snare }

        void Show(string text, HudKind kind = HudKind.Info)
        {
            if (forceKind) kind = forcedKind;   // 允许在 Inspector 强制预览配色

            ApplyGradient(kind);                 // 强制写入渐变
            uiText.text = text;

            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(ShowThenHide(kind));
        }


        void ApplyGradient(HudKind kind)
        {
            if (!uiText) return;

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
            var g = kind switch
            {
                HudKind.Energy => gradientEnergy,
                HudKind.Time => gradientTime,
                HudKind.Snare => gradientSnare,
                _ => gradientInfo
            };
            uiText.colorGradient = g.ToVertexGradient();

            // 刷新网格
            uiText.SetVerticesDirty();
            uiText.SetLayoutDirty();
        }


        IEnumerator ShowThenHide(HudKind _)
        {
            SetVisible(true, true);

            Vector2 basePos = root.anchoredPosition;
            Vector3 baseScale = root.localScale;
            float t = 0f;

            while (t < showSeconds)
            {
                t += Time.unscaledDeltaTime;

                if (enableShake)
                {
                    float nx = (Mathf.PerlinNoise(31f, t * shakeFrequency) - 0.5f) * 2f;
                    float ny = (Mathf.PerlinNoise(73f, t * shakeFrequency) - 0.5f) * 2f;
                    root.anchoredPosition = basePos + new Vector2(nx, ny) * shakeAmplitude;
                }

                if (enablePulse)
                {
                    // 正弦脉冲：1 ± amplitude，保持各向等比
                    float s = 1f + Mathf.Sin(t * Mathf.PI * 2f * pulseFrequency) * pulseAmplitude;
                    root.localScale = new Vector3(s, s, 1f);
                }

                yield return null;
            }

            // 复位
            root.anchoredPosition = basePos;
            root.localScale = baseScale;

            if (!fadeOut)
            {
                SetVisible(false);
                _co = null;
                yield break;
            }

            // 淡出
            float d = 0f;
            const float duration = 0.25f;
            while (d < duration)
            {
                d += Time.unscaledDeltaTime;
                if (_canvasGroup) _canvasGroup.alpha = Mathf.Lerp(1f, 0f, d / duration);
                yield return null;
            }

            SetVisible(false);
            _co = null;
        }

        void SetVisible(bool visible, bool resetAlpha = false)
        {
            if (!root) return;
            if (resetAlpha && _canvasGroup) _canvasGroup.alpha = 1f;
            root.gameObject.SetActive(visible);
        }

        // ---------- mapping helpers ----------
        HudKind MapKindForMove(MoveBlockReason reason, string msg)
        {
            return reason switch
            {
                MoveBlockReason.NotEnoughResource => HudKind.Energy,
                MoveBlockReason.NoBudget => HudKind.Time,
                MoveBlockReason.Entangled => HudKind.Snare,
                _ => HeuristicByText(msg)
            };
        }

        HudKind MapKindForAttack(AttackRejectReasonV2 reason, string msg)
        {
            return reason switch
            {
                AttackRejectReasonV2.NotEnoughResource => HudKind.Energy,
                AttackRejectReasonV2.OnCooldown => HudKind.Info,
                AttackRejectReasonV2.NoPath => HudKind.Info,
                AttackRejectReasonV2.CantMove => HudKind.Info,
                _ => HeuristicByText(msg)
            };
        }

        HudKind HeuristicByText(string msg)
        {
            if (msg.IndexOf("energy", System.StringComparison.OrdinalIgnoreCase) >= 0) return HudKind.Energy;
            if (msg.IndexOf("time", System.StringComparison.OrdinalIgnoreCase) >= 0) return HudKind.Time;
            if (msg.IndexOf("entangle", System.StringComparison.OrdinalIgnoreCase) >= 0
             || msg.IndexOf("can't move", System.StringComparison.OrdinalIgnoreCase) >= 0) return HudKind.Snare;
            return HudKind.Info;
        }

        // ---------- gradient helper ----------
        [System.Serializable]
        public struct HudGradient
        {
            public Color topLeft, topRight, bottomLeft, bottomRight;

            public VertexGradient ToVertexGradient()
                => new VertexGradient(topLeft, topRight, bottomLeft, bottomRight);

            public static HudGradient InfoDefault()
     // Tiffany Blue 系：上浅下深，偏清爽
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
                return new HudGradient { topLeft = ctl, topRight = ctr, bottomLeft = cbl, bottomRight = cbr };
            }
        }
    }
}
