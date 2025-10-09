// File: TGD.CoreV2/Runtime/MoveRateStatusRuntime.cs
using System.Collections.Generic;
using UnityEngine;

namespace TGD.CoreV2
{
    [DisallowMultipleComponent]
    public sealed class MoveRateStatusRuntime : MonoBehaviour
    {
        [System.Serializable]
        public sealed class Mod
        {
            public string tag;
            public float multiplier = 1f;
            public float secondsLeft = -1f;
        }

        readonly Dictionary<string, Mod> _modsByTag = new();

        public IEnumerable<float> GetActiveMultipliers()
        {
            foreach (var kv in _modsByTag)
            {
                var m = kv.Value;
                if (m.multiplier > 0f && (m.secondsLeft < 0f || m.secondsLeft > 0f))
                    yield return m.multiplier;
            }
        }

        public float GetProduct()
        {
            float p = 1f;
            foreach (var m in GetActiveMultipliers()) p *= Mathf.Clamp(m, 0.01f, 100f);
            return Mathf.Clamp(p, 0.01f, 100f);
        }

        public void ApplyOrRefresh(string tag, float multiplier, int durationTurns)
        {
            if (string.IsNullOrEmpty(tag) || multiplier <= 0f) return;

            float secs = (durationTurns < 0) ? -1f : durationTurns * StatsMathV2.BaseTurnSeconds;

            if (_modsByTag.TryGetValue(tag, out var exist))
            {
                if (exist.secondsLeft >= 0f && secs >= 0f)
                    exist.secondsLeft = Mathf.Max(exist.secondsLeft, secs);
                else
                    exist.secondsLeft = secs;
                exist.multiplier = multiplier;
            }
            else
            {
                _modsByTag[tag] = new Mod { tag = tag, multiplier = multiplier, secondsLeft = secs };
            }
        }

        public void ApplyOrRefreshExclusive(string tag, float multiplier, int durationTurns)
        {
            if (string.IsNullOrEmpty(tag)) return;

            float clamped = Mathf.Clamp(multiplier, 0.01f, 100f);
            if (Mathf.Approximately(clamped, 1f)) return;

            bool isHaste = clamped > 1f;
            var toRemove = new List<string>();
            foreach (var kv in _modsByTag)
            {
                if (kv.Key == tag) continue;
                var mod = kv.Value;
                if (mod == null) continue;

                float m = mod.multiplier;
                bool modIsHaste = m > 1f + 1e-4f;
                bool modIsSlow = m < 1f - 1e-4f;
                if (isHaste && modIsHaste) toRemove.Add(kv.Key);
                else if (!isHaste && modIsSlow) toRemove.Add(kv.Key);
            }

            for (int i = 0; i < toRemove.Count; i++)
                _modsByTag.Remove(toRemove[i]);

            ApplyOrRefresh(tag, clamped, durationTurns);
        }

        public void ApplyStickyMultiplier(float multiplier, int durationTurns)
        {
            ApplyOrRefreshExclusive("Untyped", multiplier, durationTurns);
        }

        public bool HasActiveTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            if (!_modsByTag.TryGetValue(tag, out var mod) || mod == null) return false;
            if (mod.multiplier <= 0f) return false;
            if (mod.secondsLeft < 0f) return true;
            return mod.secondsLeft > 0f;
        }
        public void ConsumeSeconds(float seconds)
        {
            if (seconds <= 0f) return;

            var keys = new List<string>(_modsByTag.Keys);
            foreach (var k in keys)
            {
                var m = _modsByTag[k];
                if (m.secondsLeft > 0f)
                {
                    m.secondsLeft = Mathf.Max(0f, m.secondsLeft - seconds);
                    if (m.secondsLeft == 0f) _modsByTag.Remove(k);
                    else _modsByTag[k] = m;
                }
            }
        }

        public void ClearAll() => _modsByTag.Clear();
    }
}
