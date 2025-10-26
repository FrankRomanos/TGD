using System.Collections.Generic;
using UnityEngine;

namespace TGD.Level
{
    public enum DamageVisualKind { Normal, Crit, Heal }

    public class DamageNumberManager : MonoBehaviour
    {
        public Camera worldCamera;
        public RectTransform canvasRoot;
        public RectTransform container;
        public DamageNumberItem itemPrefab;

        public int prewarm = 16;
        readonly Queue<DamageNumberItem> pool = new();

        static DamageNumberManager _inst;
        void OnEnable() { _inst = this; }
        void OnDisable() { if (_inst == this) _inst = null; }

        void Awake()
        {
            if (!worldCamera) worldCamera = Camera.main;
            if (!canvasRoot) canvasRoot = GetComponentInParent<Canvas>(true)?.GetComponent<RectTransform>();
            if (!container) container = (RectTransform)transform;

            for (int i = 0; i < Mathf.Max(prewarm, 1); i++)
                pool.Enqueue(Instantiate(itemPrefab, container));
        }

        DamageNumberItem Get() => pool.Count > 0 ? pool.Dequeue() : Instantiate(itemPrefab, container);
        public void Recycle(DamageNumberItem it) { it.gameObject.SetActive(false); pool.Enqueue(it); }

        public void Show(Vector3 worldPos, int amount, DamageVisualKind kind = DamageVisualKind.Normal, float scale = 1f)
        {
            if (!worldCamera) worldCamera = Camera.main;
            if (!canvasRoot || !itemPrefab) return;

            Vector2 screen = worldCamera.WorldToScreenPoint(worldPos);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot, screen, null, out var anchored);

            var it = Get();
            it.Setup(this, kind, amount, scale);
            var rt = (RectTransform)it.transform;
            rt.anchoredPosition = anchored + new Vector2(Random.Range(-8f, 8f), Random.Range(-6f, 6f));
            it.gameObject.SetActive(true);
            it.Play();
        }

        public static void ShowAt(Vector3 worldPos, int amount, DamageVisualKind kind = DamageVisualKind.Normal, float scale = 1f)
            => _inst?.Show(worldPos, amount, kind, scale);
    }
}
