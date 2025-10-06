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
            public string tag;           // ͬԴȥ��Key������ "Patch@q,r" �� "Hazard@id@q,r"��
            public float multiplier = 1f;   // ���� 0.8f, 1.2f
            public float secondsLeft = -1f; // <0 ���ã�>=0 �𲽵ݼ�
        }

        readonly Dictionary<string, Mod> _modsByTag = new();

        /// ��ǰ���С������͡����εĳ�����>0��
        public IEnumerable<float> GetActiveMultipliers()
        {
            foreach (var kv in _modsByTag)
            {
                var m = kv.Value;
                if (m.multiplier > 0f && (m.secondsLeft < 0f || m.secondsLeft > 0f))
                    yield return m.multiplier;
            }
        }

        /// ��������ֵ������/UI���ã�
        public float GetProduct()
        {
            float p = 1f;
            foreach (var m in GetActiveMultipliers()) p *= Mathf.Clamp(m, 0.01f, 100f);
            return Mathf.Clamp(p, 0.01f, 100f);
        }

        /// ��ӻ�ˢ��ͬԴ������durationTurns<0=���ã������룩
        public void ApplyOrRefresh(string tag, float multiplier, int durationTurns)
        {
            if (string.IsNullOrEmpty(tag) || multiplier <= 0f) return;

            float secs = (durationTurns < 0) ? -1f : durationTurns * StatsMathV2.BaseTurnSeconds;

            if (_modsByTag.TryGetValue(tag, out var exist))
            {
                // ˢ�³���
                if (exist.secondsLeft >= 0f && secs >= 0f)
                    exist.secondsLeft = Mathf.Max(exist.secondsLeft, secs);
                else
                    exist.secondsLeft = secs; // ���ø�����ʱ����֮
                exist.multiplier = multiplier; // ͬԴ���±���
            }
            else
            {
                _modsByTag[tag] = new Mod { tag = tag, multiplier = multiplier, secondsLeft = secs };
            }
        }

        /// ���ݾɵ��ã��� tag ʱ�á�Untyped�����ࣨ�����飩
        public void ApplyStickyMultiplier(float multiplier, int durationTurns)
        {
            ApplyOrRefresh("Untyped", multiplier, durationTurns);
        }

        /// �۳���������������������ʱ���ã�
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
