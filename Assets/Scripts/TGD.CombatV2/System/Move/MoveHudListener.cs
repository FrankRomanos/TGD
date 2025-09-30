// File: Assets/Scripts/TGD.UI/MoveHudListenerTMP.cs
using System;
using System.Collections;
using TGD.HexBoard;
using TMPro;
using UnityEngine;

namespace TGD.CombatV2
{
    /// ���� HexMoveEvents���� TextMeshPro ����Ϣ��ʾ
    [DisallowMultipleComponent]
    public sealed class MoveHudListenerTMP : MonoBehaviour
    {
        [Header("Targets")]
        [Tooltip("Ҫ��ʾ���ֵ� TMP_Text")]
        public TMP_Text uiText;

        [Tooltip("��������������� CanvasGroup ������������������ uiText �� Transform")]
        public RectTransform root;

        [Header("Behavior")]
        [Min(0.2f)] public float showSeconds = 1.6f;
        public bool fadeOut = true;

        CanvasGroup _cg;
        Coroutine _co;

        void Awake()
        {
            if (!uiText) uiText = GetComponentInChildren<TMP_Text>(true);
            if (!root) root = (uiText ? uiText.rectTransform : null);
            if (root) _cg = root.GetComponent<CanvasGroup>() ?? root.gameObject.AddComponent<CanvasGroup>();
            SetVisible(false, true);
        }

        void OnEnable()
        {
            HexMoveEvents.MoveRejected += OnRejected;
            HexMoveEvents.TimeRefunded += OnRefunded;
            // ��Ը��Ҳ������ʾ�����¼���
            // TGD.HexBoard.HexMoveEvents.RangeShown += OnRangeShown;
            // TGD.HexBoard.HexMoveEvents.RangeHidden += _ => SetVisible(false);
        }

        void OnDisable()
        {
            HexMoveEvents.MoveRejected -= OnRejected;
            HexMoveEvents.TimeRefunded -= OnRefunded;
            // HexMoveEvents.RangeShown  / RangeHidden ����ҪôҲ�˶�
        }

        private void OnRefunded(Unit u, int sec)
        {
            if (!uiText || !root) return;
            uiText.text = $"+{sec}s refunded";
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(ShowThenHide());
        }

        void OnRejected(TGD.HexBoard.Unit unit, MoveBlockReason reason, string msg)
        {
            if (!uiText || !root) return;

            if (string.IsNullOrEmpty(msg))
            {
                // ����Ӣ��
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

            float t = 0f;
            const float dur = 0.25f;
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
