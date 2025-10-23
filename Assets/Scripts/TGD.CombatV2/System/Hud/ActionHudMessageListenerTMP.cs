using System.Collections;
using TMPro;
using TGD.HexBoard;
using UnityEngine;
using Unit = TGD.HexBoard.Unit;

namespace TGD.CombatV2
{
    /// <summary>
    /// Consolidated HUD listener for move/attack rejections and refunds.
    /// Replaces the legacy MoveHudListenerTMP/AttackHudListenerTMP pair without changing behaviour.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActionHudMessageListenerTMP : MonoBehaviour
    {
        [Header("Targets")]
        public TMP_Text uiText;
        public RectTransform root;

        [Header("Filter")]
        public HexBoardTestDriver driver;
        public bool requireUnitMatch = true;

        [Header("Sources")]
        public bool listenMove = true;
        public bool listenAttack = true;

        [Header("Behaviour")]
        [Min(0.2f)] public float showSeconds = 1.6f;
        public bool fadeOut = true;

        CanvasGroup _canvasGroup;
        Coroutine _co;

        void Reset()
        {
            if (!uiText) uiText = GetComponentInChildren<TMP_Text>(true);
            if (!root && uiText) root = uiText.rectTransform;
            if (!driver) driver = GetComponentInParent<HexBoardTestDriver>();
        }

        void Awake()
        {
            if (!uiText) uiText = GetComponentInChildren<TMP_Text>(true);
            if (!root && uiText) root = uiText.rectTransform;
            if (root) _canvasGroup = root.GetComponent<CanvasGroup>() ?? root.gameObject.AddComponent<CanvasGroup>();
            if (!driver) driver = GetComponentInParent<HexBoardTestDriver>();
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
        }

        Unit UnitRef => driver ? driver.UnitRef : null;
        bool Matches(Unit u) => !requireUnitMatch || (u != null && u == UnitRef);

        void OnMoveRefunded(Unit unit, int seconds)
        {
            if (!listenMove || !Matches(unit) || !uiText || !root)
                return;

            Show($"+{seconds}s refunded");
        }

        void OnMoveRejected(Unit unit, MoveBlockReason reason, string message)
        {
            if (!listenMove || !Matches(unit) || !uiText || !root)
                return;

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

            Show(message);
        }

        void OnAttackRejected(Unit unit, AttackRejectReasonV2 reason, string message)
        {
            if (!listenAttack || !Matches(unit) || !uiText || !root)
                return;

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

            Show(message);
        }

        void OnAttackMiss(Unit unit, string message)
        {
            if (!listenAttack || !Matches(unit) || !uiText || !root)
                return;

            Show(string.IsNullOrEmpty(message) ? "Attack missed." : message);
        }

        void Show(string text)
        {
            uiText.text = text;
            if (_co != null)
                StopCoroutine(_co);
            _co = StartCoroutine(ShowThenHide());
        }

        IEnumerator ShowThenHide()
        {
            SetVisible(true, true);
            yield return new WaitForSeconds(showSeconds);

            if (!fadeOut)
            {
                SetVisible(false);
                _co = null;
                yield break;
            }

            float t = 0f;
            const float duration = 0.25f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                if (_canvasGroup)
                    _canvasGroup.alpha = Mathf.Lerp(1f, 0f, t / duration);
                yield return null;
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
        }
    }
}
