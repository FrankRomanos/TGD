using System;
using System.Collections;
using TGD.HexBoard;
using TMPro;
using UnityEngine;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class MoveHudListenerTMP : MonoBehaviour
    {
        [Header("Targets")]
        public TMP_Text uiText;
        public RectTransform root;

        [Header("Filter")]
        public HexBoardTestDriver driver;   // ★ 新增：只显示属于自己的事件

        [Header("Behavior")]
        [Min(0.2f)] public float showSeconds = 1.6f;
        public bool fadeOut = true;

        CanvasGroup _cg;
        Coroutine _co;

        void Awake()
        {
            if (!uiText) uiText = GetComponentInChildren<TMP_Text>(true);
            if (!root && uiText) root = uiText.rectTransform;
            if (root) _cg = root.GetComponent<CanvasGroup>() ?? root.gameObject.AddComponent<CanvasGroup>();
            if (!driver) driver = GetComponentInParent<HexBoardTestDriver>(); // ★
            SetVisible(false, true);
        }

        void OnEnable()
        {
            HexMoveEvents.MoveRejected += OnRejected;
            HexMoveEvents.TimeRefunded += OnRefunded;
        }

        void OnDisable()
        {
            HexMoveEvents.MoveRejected -= OnRejected;
            HexMoveEvents.TimeRefunded -= OnRefunded;
            if (_co != null) { StopCoroutine(_co); _co = null; }
            SetVisible(false);
        }

        bool Match(Unit u) => driver && driver.UnitRef == u;

        void OnRefunded(Unit u, int sec)
        {
            if (!Match(u) || !uiText || !root) return;
            uiText.text = $"+{sec}s refunded";
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(ShowThenHide());
        }

        void OnRejected(Unit u, MoveBlockReason reason, string msg)
        {
            if (!Match(u) || !uiText || !root) return;

            if (string.IsNullOrEmpty(msg))
            {
                msg = reason switch
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
            uiText.text = msg;
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(ShowThenHide());
        }

        IEnumerator ShowThenHide()
        {
            SetVisible(true, true);
            yield return new WaitForSeconds(showSeconds);

            if (!fadeOut) { SetVisible(false); yield break; }

            float t = 0f, dur = 0.25f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                if (_cg) _cg.alpha = Mathf.Lerp(1f, 0f, t / dur);
                yield return null;
            }
            SetVisible(false);
        }

        void SetVisible(bool v, bool resetAlpha = false)
        {
            if (!root) return;
            if (resetAlpha && _cg) _cg.alpha = 1f;
            root.gameObject.SetActive(v);
        }
    }
}
