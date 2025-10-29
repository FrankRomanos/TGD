using UnityEngine;

namespace TGD.UIV2
{
    /// <summary>
    /// Keeps an overlay rect (outside the scrolling mask) aligned with a target slot image.
    /// </summary>
    [ExecuteAlways]
    public sealed class TurnTimelineOverlayFollower : MonoBehaviour
    {
        public RectTransform target;
        public RectTransform overlay;
        public Vector2 offset;

        Canvas _cachedCanvas;

        void Awake()
        {
            if (!overlay)
                overlay = GetComponent<RectTransform>();
        }

        void LateUpdate()
        {
            if (!target || !overlay)
                return;

            var parent = overlay.parent as RectTransform;
            if (!parent)
                return;

            var canvas = ResolveCanvas();
            if (!canvas)
                return;

            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var rect = target.rect;
            var localCenter = new Vector3(rect.center.x, rect.center.y, 0f);
            var world = target.TransformPoint(localCenter);
            var screen = RectTransformUtility.WorldToScreenPoint(cam, world);

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out var local))
                overlay.anchoredPosition = local + offset;
        }

        public void SetTarget(RectTransform newTarget)
        {
            target = newTarget;
        }

        Canvas ResolveCanvas()
        {
            if (_cachedCanvas)
                return _cachedCanvas;

            _cachedCanvas = GetComponentInParent<Canvas>();
            return _cachedCanvas;
        }
    }
}

