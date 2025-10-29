using TMPro;
using UnityEngine;

namespace TGD.UIV2
{
    /// <summary>
    /// Wraps overlay label state so the controller can hide/show text and attach it to slots.
    /// </summary>
    public sealed class TurnTimelineOverlayBinding : MonoBehaviour
    {
        public TMP_Text label;
        public CanvasGroup canvasGroup;
        public TurnTimelineOverlayFollower follower;

        void Awake()
        {
            if (!label)
                label = GetComponentInChildren<TMP_Text>();
            if (!canvasGroup)
                canvasGroup = GetComponent<CanvasGroup>();
            if (!follower)
                follower = GetComponent<TurnTimelineOverlayFollower>();
        }

        public void AttachTo(RectTransform target)
        {
            if (follower)
            {
                follower.SetTarget(target);
                follower.enabled = target != null;
            }
        }

        public void SetText(string value)
        {
            if (label)
                label.text = value ?? string.Empty;
        }

        public void HideImmediate()
        {
            if (label)
                label.text = string.Empty;

            if (canvasGroup)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }

        public void Detach()
        {
            if (follower)
            {
                follower.SetTarget(null);
                follower.enabled = false;
            }

            HideImmediate();
        }
    }
}

