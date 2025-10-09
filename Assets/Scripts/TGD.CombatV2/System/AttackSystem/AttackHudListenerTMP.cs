using System.Collections;
using TMPro;
using TGD.HexBoard;
using UnityEngine;

namespace TGD.CombatV2
{
    /// <summary>
    /// Displays attack related HUD hints driven by <see cref="AttackEventsV2"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AttackHudListenerTMP : MonoBehaviour
    {
        [Header("Targets")]
        public TMP_Text uiText;
        public RectTransform root;

        [Header("Behavior")]
        [Min(0.2f)] public float showSeconds = 1.6f;
        public bool fadeOut = true;

        CanvasGroup _canvasGroup;
        Coroutine _co;

        void Reset()
        {
            if (!uiText) uiText = GetComponentInChildren<TMP_Text>(true);
            if (!root && uiText) root = uiText.rectTransform;
        }

        void Awake()
        {
            if (!uiText) uiText = GetComponentInChildren<TMP_Text>(true);
            if (!root && uiText) root = uiText.rectTransform;
            if (root) _canvasGroup = root.GetComponent<CanvasGroup>() ?? root.gameObject.AddComponent<CanvasGroup>();
            SetVisible(false, true);
        }

        void OnEnable()
        {
            AttackEventsV2.AttackRejected += OnRejected;
            AttackEventsV2.AttackMiss += OnMiss;
        }

        void OnDisable()
        {
            AttackEventsV2.AttackRejected -= OnRejected;
            AttackEventsV2.AttackMiss -= OnMiss;
            if (_co != null)
            {
                StopCoroutine(_co);
                _co = null;
            }
            SetVisible(false);
        }

        void OnRejected(Unit unit, AttackRejectReasonV2 reason, string message)
        {
            if (!uiText || !root) return;

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

        void OnMiss(Unit unit, string message)
        {
            if (!uiText || !root) return;
            if (string.IsNullOrEmpty(message)) message = "Attack missed.";
            Show(message);
        }

        void Show(string text)
        {
            uiText.text = text;
            if (_co != null) StopCoroutine(_co);
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
                if (_canvasGroup) _canvasGroup.alpha = Mathf.Lerp(1f, 0f, t / duration);
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
    }
}