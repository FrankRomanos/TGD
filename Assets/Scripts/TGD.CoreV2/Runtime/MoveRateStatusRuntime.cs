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
            public string tag;           // 同源去重Key（例如 "Patch@q,r" 或 "Hazard@id@q,r"）
            public float multiplier = 1f;   // 例如 0.8f, 1.2f
            public float secondsLeft = -1f; // <0 永久；>=0 逐步递减
        }

        readonly Dictionary<string, Mod> _modsByTag = new();

        /// 当前所有“持续型”修饰的乘数（>0）
        public IEnumerable<float> GetActiveMultipliers()
        {
            foreach (var kv in _modsByTag)
            {
                var m = kv.Value;
                if (m.multiplier > 0f && (m.secondsLeft < 0f || m.secondsLeft > 0f))
                    yield return m.multiplier;
            }
        }

        /// 乘数连乘值（调试/UI可用）
        public float GetProduct()
        {
            float p = 1f;
            foreach (var m in GetActiveMultipliers()) p *= Mathf.Clamp(m, 0.01f, 100f);
            return Mathf.Clamp(p, 0.01f, 100f);
        }

        /// 添加或刷新同源贴附（durationTurns<0=永久；否则按秒）
        public void ApplyOrRefresh(string tag, float multiplier, int durationTurns)
        {
            if (string.IsNullOrEmpty(tag) || multiplier <= 0f) return;

            float secs = (durationTurns < 0) ? -1f : durationTurns * StatsMathV2.BaseTurnSeconds;

            if (_modsByTag.TryGetValue(tag, out var exist))
            {
                // 刷新持续
                if (exist.secondsLeft >= 0f && secs >= 0f)
                    exist.secondsLeft = Mathf.Max(exist.secondsLeft, secs);
                else
                    exist.secondsLeft = secs; // 永久覆盖临时，或反之
                exist.multiplier = multiplier; // 同源更新倍率
            }
            else
            {
                _modsByTag[tag] = new Mod { tag = tag, multiplier = multiplier, secondsLeft = secs };
            }
        }

        /// 兼容旧调用：无 tag 时用“Untyped”聚类（不建议）
        public void ApplyStickyMultiplier(float multiplier, int durationTurns)
        {
            ApplyOrRefresh("Untyped", multiplier, durationTurns);
        }

        /// 扣除已消耗秒数（动作结束时调用）
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
