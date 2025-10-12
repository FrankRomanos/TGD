using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.CoreV2
{
    /// <summary>
    /// 秒制冷却仓库：以技能 id 为键，记录剩余秒数。
    /// </summary>
    [Serializable]
    public sealed class CooldownStoreSecV2
    {
        readonly Dictionary<string, int> _seconds = new();

        public void StartSeconds(string skillId, int seconds)
        {
            if (string.IsNullOrEmpty(skillId)) return;
            _seconds[skillId] = seconds;
        }

        public int AddSeconds(string skillId, int deltaSeconds)
        {
            if (string.IsNullOrEmpty(skillId)) return 0;
            int current = 0;
            _seconds.TryGetValue(skillId, out current);
            current += deltaSeconds;
            _seconds[skillId] = current;
            return current;
        }

        public int SecondsLeft(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return 0;
            return _seconds.TryGetValue(skillId, out var seconds) ? seconds : 0;
        }

        public int TurnsLeft(string skillId)
        {
            int sec = SecondsLeft(skillId);
            return StatsMathV2.CooldownToTurns(sec);
        }

        public bool Ready(string skillId)
        {
            return SecondsLeft(skillId) <= 0;
        }

        public int TickEndTurn(string skillId, int baseTurnSeconds = StatsMathV2.BaseTurnSeconds)
        {
            return AddSeconds(skillId, -Mathf.Max(0, baseTurnSeconds));
        }

        public IEnumerable<KeyValuePair<string, int>> Entries => _seconds;
        public IEnumerable<string> Keys => _seconds.Keys;
    }
}
