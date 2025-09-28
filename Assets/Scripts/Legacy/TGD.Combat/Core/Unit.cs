using System;
using System.Collections.Generic;
using System.Linq;
using TGD.Grid;
using TGD.Core;
using TGD.Data;

namespace TGD.Combat
{
    [Serializable]
    public class Unit
    {
        public string UnitId;
        public string ClassId;
        public int TeamId;
        public Stats Stats = new Stats();
        public HexCoord Position;


        public List<SkillDefinition> Skills = new();
        private readonly Dictionary<string, int> _cdSeconds = new();
        private readonly List<StatusInstance> _statuses = new();
        int _skillsRevision;

        public IReadOnlyList<StatusInstance> Statuses => _statuses;
        public int SkillsRevision => _skillsRevision;

        public void NotifySkillsChanged()
        {
            unchecked
            {
                _skillsRevision++;
            }
        }

        public void SetCooldown(SkillDefinition skill)
        {
            if (skill == null || string.IsNullOrWhiteSpace(skill.skillID))
                return;
            _cdSeconds[skill.skillID] = Math.Max(0, skill.cooldownSeconds);
        }
        public int GetCooldownSeconds(string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
                return 0;
            return _cdSeconds.TryGetValue(skillId, out var seconds) ? Math.Max(0, seconds) : 0;
        }

        public void SetCooldownSeconds(string skillId, int seconds)
        {
            if (string.IsNullOrWhiteSpace(skillId))
                return;
            _cdSeconds[skillId] = Math.Max(0, seconds);
        }

        public void ClearCooldown(string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
                return;
            _cdSeconds.Remove(skillId);
        }

        public void TickCooldownSeconds(int deltaSeconds = CombatClock.BaseTurnSeconds)
        {
            if (_cdSeconds.Count == 0)
                return;

            foreach (var key in _cdSeconds.Keys.ToList())
                _cdSeconds[key] = Math.Max(0, _cdSeconds[key] - deltaSeconds);
        }

        public int TurnTime => CombatClock.BaseTurnSeconds + Stats.Speed;
        public int PrepaidTime;
        public int RemainingTime;

        public void StartTurn()
        {
            RemainingTime = TurnTime - PrepaidTime;
            if (RemainingTime < 0)
                RemainingTime = 0;
            PrepaidTime = 0;
        }

        public void EndTurn()
        {
            RemainingTime = 0;
            PrepaidTime = 0;
        }

        public void SpendTime(int seconds)
        {
            if (seconds <= 0)
                return;

            RemainingTime -= seconds;
            if (RemainingTime < 0)
            {
                PrepaidTime += -RemainingTime;
                RemainingTime = 0;
            }
        }

        public void RefundTime(int seconds)
        {
            if (seconds <= 0)
                return;

            RemainingTime += seconds;
            if (PrepaidTime > 0)
            {
                int refund = Math.Min(PrepaidTime, RemainingTime);
                PrepaidTime -= refund;
                RemainingTime -= refund;
            }
        }

        public bool IsOnCooldown(SkillDefinition skill)
        {
            if (skill == null || string.IsNullOrWhiteSpace(skill.skillID))
                return false;
            return _cdSeconds.TryGetValue(skill.skillID, out var seconds) && seconds > 0;
        }

        public int GetUiTurns(SkillDefinition skill)
        {
            if (skill == null || string.IsNullOrWhiteSpace(skill.skillID))
                return 0;
            return (int)Math.Ceiling((_cdSeconds.TryGetValue(skill.skillID, out var sec) ? sec : 0) / (float)CombatClock.BaseTurnSeconds);
        }

        public int GetUiRounds(SkillDefinition skill)
        {
            if (skill == null || string.IsNullOrWhiteSpace(skill.skillID))
                return 0;
            return (int)Math.Ceiling((_cdSeconds.TryGetValue(skill.skillID, out var sec) ? sec : 0) / (2f * CombatClock.BaseTurnSeconds));
        }

        public bool IsAllyOf(Unit other) => other != null && TeamId == other.TeamId;
        public bool IsEnemyOf(Unit other) => other != null && TeamId != other.TeamId;

        public void ModifyCooldown(string skillId, int deltaSeconds)
        {
            if (string.IsNullOrWhiteSpace(skillId) || deltaSeconds == 0)
                return;

            if (!_cdSeconds.TryGetValue(skillId, out var seconds))
                seconds = 0;

            seconds = Math.Max(0, seconds + deltaSeconds);
            _cdSeconds[skillId] = seconds;
        }

        public StatusInstance FindStatus(string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
                return null;
            return _statuses.FirstOrDefault(s => string.Equals(s.StatusSkillId, skillId, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<StatusInstance> FindStatuses(IEnumerable<string> skillIds)
        {
            if (skillIds == null)
                yield break;

            var set = new HashSet<string>(skillIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
            if (set.Count == 0)
                yield break;

            foreach (var status in _statuses)
            {
                if (set.Contains(status.StatusSkillId))
                    yield return status;
            }
        }

        public void AddStatus(StatusInstance instance)
        {
            if (instance == null)
                return;
            if (!_statuses.Contains(instance))
                _statuses.Add(instance);
        }

        public void RemoveStatus(StatusInstance instance)
        {
            if (instance == null)
                return;
            _statuses.Remove(instance);
        }

        public SkillDefinition FindSkill(string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
                return null;
            return Skills.FirstOrDefault(s => string.Equals(s?.skillID, skillId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
