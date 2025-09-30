// File: TGD.CoreV2/Runtime/MoveRateStatusRuntime.cs
using System.Collections.Generic;
using UnityEngine;

namespace TGD.CoreV2
{
    /// ����ʱ�������������ɹ����� UnitRuntimeContext ͬһ�����ϣ�
    [DisallowMultipleComponent]
    public sealed class MoveRateStatusRuntime : MonoBehaviour
    {
        [System.Serializable]
        public sealed class Mod
        {
            public float multiplier = 1f;   // ���� 0.8f, 1.2f
            public float secondsLeft = -1f; // <0 ��ʾ���ã�������ݼ�
        }

        readonly List<Mod> _mods = new();

        /// ��ǰ���С������͡����εı���ö�٣�>0��
        public IEnumerable<float> GetActiveMultipliers()
        {
            for (int i = 0; i < _mods.Count; i++)
            {
                var m = _mods[i];
                if (m.multiplier > 0f && (m.secondsLeft < 0f || m.secondsLeft > 0f))
                    yield return m.multiplier;
            }
        }

        /// ��ӻ�ˢ��һ������ԡ�����/���٣�durationTurns<0 ��ʾ����
        public void ApplyStickyMultiplier(float multiplier, int durationTurns)
        {
            if (multiplier <= 0f) return;
            float secs = (durationTurns < 0) ? -1f : durationTurns * StatsMathV2.BaseTurnSeconds;
            _mods.Add(new Mod { multiplier = multiplier, secondsLeft = secs });
        }

        /// �۳������ĵ���������������ʱ���ã�
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

        /// ��գ������ã�
        public void ClearAll() => _mods.Clear();
    }
}

