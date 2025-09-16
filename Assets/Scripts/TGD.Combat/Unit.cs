using System.Collections.Generic;
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
        private readonly Dictionary<string, int> cooldownRounds = new();

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
            var keys = new List<string>(cooldownRounds.Keys);
            foreach (var k in keys)
            {
                cooldownRounds[k] = System.Math.Max(0, cooldownRounds[k] - 1);
            }
        }

        public bool IsOnCooldown(SkillDefinition s) =>
            cooldownRounds.TryGetValue(s.skillID, out var r) && r > 0;

        public void SetCooldown(SkillDefinition s)
        {
            int rounds = CombatClock.CooldownToRounds(s.cooldownSeconds);
            cooldownRounds[s.skillID] = rounds;
        }
    }
}
