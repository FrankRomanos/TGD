// File: TGD.CoreV2/Runtime/MoveRateStatusRuntime.cs
using System.Collections.Generic;
using UnityEngine;

namespace TGD.CoreV2
{
    /// 运行时移速修饰器（可挂在与 UnitRuntimeContext 同一对象上）
    [DisallowMultipleComponent]
    public sealed class MoveRateStatusRuntime : MonoBehaviour
    {
        [System.Serializable]
        public sealed class Mod
        {
            public float multiplier = 1f;   // 例如 0.8f, 1.2f
            public float secondsLeft = -1f; // <0 表示永久；否则按秒递减
        }

        readonly List<Mod> _mods = new();

        /// 当前所有“持续型”修饰的倍率枚举（>0）
        public IEnumerable<float> GetActiveMultipliers()
        {
            for (int i = 0; i < _mods.Count; i++)
            {
                var m = _mods[i];
                if (m.multiplier > 0f && (m.secondsLeft < 0f || m.secondsLeft > 0f))
                    yield return m.multiplier;
            }
        }

        /// 添加或刷新一个“黏性”减速/加速；durationTurns<0 表示永久
        public void ApplyStickyMultiplier(float multiplier, int durationTurns)
        {
            if (multiplier <= 0f) return;
            float secs = (durationTurns < 0) ? -1f : durationTurns * StatsMathV2.BaseTurnSeconds;
            _mods.Add(new Mod { multiplier = multiplier, secondsLeft = secs });
        }

        /// 扣除已消耗的秒数（动作结束时调用）
        public void ConsumeSeconds(float seconds)
        {
            if (seconds <= 0f) return;
            for (int i = 0; i < _mods.Count; i++)
            {
                if (_mods[i].secondsLeft > 0f)
                    _mods[i].secondsLeft = Mathf.Max(0f, _mods[i].secondsLeft - seconds);
            }
            _mods.RemoveAll(m => m.secondsLeft == 0f);
        }

        /// 清空（测试用）
        public void ClearAll() => _mods.Clear();
    }
}

