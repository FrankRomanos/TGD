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

        // 技能与冷却
        public List<SkillDefinition> Skills = new List<SkillDefinition>();
        private readonly Dictionary<string, int> _cdSeconds = new();

        // 施放技能时设定冷却（以秒为准）
        public void SetCooldown(SkillDefinition s)
        {
            _cdSeconds[s.skillID] = Math.Max(0, s.cooldownSeconds);
        }

        // 任意回合结束时统一调用：-6s
        public void TickCooldownSeconds(int deltaSeconds = 6)
        {
            if (_cdSeconds.Count == 0) return;
            foreach (var key in _cdSeconds.Keys.ToList())
            {
                _cdSeconds[key] = Math.Max(0, _cdSeconds[key] - deltaSeconds);
            }
        }


        // 回合时间
        public int TurnTime => CombatClock.BaseTurnSeconds + Stats.Speed;
        public int PrepaidTime;   // 敌回合 Reaction 预支
        public int RemainingTime; // 自己回合剩余时间

        public void StartTurn()
        {
            RemainingTime = TurnTime - PrepaidTime;
            if (RemainingTime < 0) RemainingTime = 0;
            PrepaidTime = 0;
        }

        public void EndTurn()
        {
            // 冷却 -1
            var keys = new List<string>(_cdSeconds.Keys);
            foreach (var k in keys)
            {
                _cdSeconds[k] = System.Math.Max(0, _cdSeconds[k] - 6);
            }
        }

        public bool IsOnCooldown(SkillDefinition s) =>
            _cdSeconds.TryGetValue(s.skillID, out var sec) && sec > 0;

        // —— 仅供 UI 显示 —— 
        public int GetUiTurns(SkillDefinition s) =>
            (int)Math.Ceiling((_cdSeconds.TryGetValue(s.skillID, out var sec) ? sec : 0) / 6.0);

        public int GetUiRounds(SkillDefinition s) =>
            (int)Math.Ceiling((_cdSeconds.TryGetValue(s.skillID, out var sec) ? sec : 0) / 12.0);

    }
}
