using System.Collections;
using TMPro;
using UnityEngine;

namespace TGD.UI
{
    public class DamageNumberItem : MonoBehaviour
    {
        [Header("Refs")] public TMP_Text text;

        [Header("Colors")]
        public Color normal = Color.white;
        public Color crit = new Color(1f, 0.85f, 0.2f);
        public Color heal = new Color(0.2f, 1f, 0.5f);

        [Header("Anim")]
        public float life = 0.8f;
        public float rise = 60f;
        public float drift = 20f;
        public float pop = 1.18f;
        public AnimationCurve alphaCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

        DamageNumberManager _mgr;
        Vector2 _spawnPos;
        float _scale;

        public void Setup(DamageNumberManager mgr, DamageVisualKind kind, int amount, float scale)
        {
            _mgr = mgr; _scale = scale;
            if (!text) text = GetComponent<TMP_Text>();

            switch (kind)
            {
                case DamageVisualKind.Crit: text.color = crit; text.text = amount.ToString(); break;
                case DamageVisualKind.Heal: text.color = heal; text.text = $"+{amount}"; break;
                default: text.color = normal; text.text = amount.ToString(); break;
            }

            transform.localScale = Vector3.one * _scale;
            _spawnPos = ((RectTransform)transform).anchoredPosition;
            SetAlpha(0f);
        }

        public void Play() => StartCoroutine(Co_Play());

        IEnumerator Co_Play()
        {
            var rt = (RectTransform)transform;

            float t = 0f, popTime = 0.12f;
            while (t < popTime)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / popTime);
                rt.localScale = Vector3.one * (_scale * Mathf.Lerp(1f, pop, k));
                SetAlpha(k * 0.85f);
                yield return null;
            }

            t = 0f;
            Vector2 start = _spawnPos;
            Vector2 end = _spawnPos + new Vector2(Random.Range(-drift, drift), rise);
            while (t < life)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / life);
                rt.anchoredPosition = Vector2.Lerp(start, end, k);
                SetAlpha(alphaCurve.Evaluate(k));
                yield return null;
            }

            _mgr.Recycle(this);
        }

        void SetAlpha(float a)
        {
            if (!text) return;
            var c = text.color; c.a = a; text.color = c;
        }
    }
}
