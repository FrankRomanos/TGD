using System;
using UnityEngine;

namespace TGD.CoreV2
{
    /// <summary>
    /// 负责管理运行时的移动速度修正（百分比/平坦/缠绕）。
    /// 将这些战斗期数据与 <see cref="StatsV2"/> 分离，避免影响出厂配置。
    /// </summary>
    [Serializable]
    public sealed class MoveRateManager
    {
        [SerializeField]
        float _percentAdd;

        [SerializeField]
        int _flatAdd;

        [SerializeField]
        bool _isEntangled;

        /// <summary>
        /// 当前移动速度百分比加成（-0.99 = -99%）。
        /// </summary>
        public float PercentAdd => _percentAdd;

        /// <summary>
        /// 当前移动速度平坦修正（允许为负）。
        /// </summary>
        public int FlatAdd => _flatAdd;

        /// <summary>
        /// 是否被缠绕（强制移动速度为 0，阻止绝大多数移动）。
        /// </summary>
        public bool IsEntangled => _isEntangled;

        /// <summary>
        /// 归一化后的乘数（用于乘以基础 MoveRate）。
        /// </summary>
        public float NormalizedMultiplier => Mathf.Clamp(1f + Mathf.Max(-0.99f, _percentAdd), 0.01f, 100f);

        /// <summary>
        /// 重置全部战斗期修正。
        /// </summary>
        public void ResetRuntime()
        {
            _percentAdd = 0f;
            _flatAdd = 0;
            _isEntangled = false;
        }

        public void SetPercentAdd(float value)
        {
            _percentAdd = Mathf.Clamp(value, -0.99f, 100f);
        }

        public void AddPercentDelta(float delta)
        {
            SetPercentAdd(_percentAdd + delta);
        }

        public void SetFlatAdd(int value)
        {
            _flatAdd = value;
        }

        public void AddFlatDelta(int delta)
        {
            _flatAdd += delta;
        }

        public void SetEntangled(bool value)
        {
            _isEntangled = value;
        }

        public void Clamp()
        {
            _percentAdd = Mathf.Clamp(_percentAdd, -0.99f, 100f);
            // 平坦加成允许任意正负数，不额外限制。
        }
    }
}
