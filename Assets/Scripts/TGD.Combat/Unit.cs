using System;
using System.Collections.Generic;
using System.Linq;
using TGD.Core;
using TGD.Data;

namespace TGD.Combat
{
    public class Unit
    {
        public string UnitId;
        public Stats Stats = new Stats();

        // ��������ȴ
        public List<SkillDefinition> Skills = new List<SkillDefinition>();
        private readonly Dictionary<string, int> _cdSeconds = new();

        // ʩ�ż���ʱ�趨��ȴ������Ϊ׼��
        public void SetCooldown(SkillDefinition s)
        {
            _cdSeconds[s.skillID] = Math.Max(0, s.cooldownSeconds);
        }

        // ����غϽ���ʱͳһ���ã�-6s
        public void TickCooldownSeconds(int deltaSeconds = 6)
        {
            if (_cdSeconds.Count == 0) return;
            foreach (var key in _cdSeconds.Keys.ToList())
            {
                _cdSeconds[key] = Math.Max(0, _cdSeconds[key] - deltaSeconds);
            }
        }


        // �غ�ʱ��
        public int TurnTime => CombatClock.BaseTurnSeconds + Stats.Speed;
        public int PrepaidTime;   // �лغ� Reaction Ԥ֧
        public int RemainingTime; // �Լ��غ�ʣ��ʱ��

        public void StartTurn()
        {
            RemainingTime = TurnTime - PrepaidTime;
            if (RemainingTime < 0) RemainingTime = 0;
            PrepaidTime = 0;
        }

        public void EndTurn()
        {
            // ��ȴ -1
            var keys = new List<string>(_cdSeconds.Keys);
            foreach (var k in keys)
            {
                _cdSeconds[k] = System.Math.Max(0, _cdSeconds[k] - 6);
            }
        }

        public bool IsOnCooldown(SkillDefinition s) =>
            _cdSeconds.TryGetValue(s.skillID, out var sec) && sec > 0;

        // ���� ���� UI ��ʾ ���� 
        public int GetUiTurns(SkillDefinition s) =>
            (int)Math.Ceiling((_cdSeconds.TryGetValue(s.skillID, out var sec) ? sec : 0) / 6.0);

        public int GetUiRounds(SkillDefinition s) =>
            (int)Math.Ceiling((_cdSeconds.TryGetValue(s.skillID, out var sec) ? sec : 0) / 12.0);

    }
}
