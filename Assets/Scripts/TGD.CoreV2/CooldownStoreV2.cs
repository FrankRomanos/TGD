using System.Collections.Generic;

namespace TGD.CoreV2
{
    public sealed class CooldownStoreV2
    {
        readonly Dictionary<string, int> _rounds = new();

        public int RoundsLeft(string id)
            => _rounds.TryGetValue(id, out var r) ? System.Math.Max(0, r) : 0;

        public void Start(string id, int rounds)
        {
            if (string.IsNullOrEmpty(id)) return;
            _rounds[id] = System.Math.Max(0, rounds);
        }

        public void Clear(string id) => _rounds.Remove(id);

        public void TickEndOfOwnerTurn()
        {
            if (_rounds.Count == 0) return;
            var keys = new List<string>(_rounds.Keys);
            foreach (var k in keys) _rounds[k] = System.Math.Max(0, _rounds[k] - 1);
        }
    }
}

