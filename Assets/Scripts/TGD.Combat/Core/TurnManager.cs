using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TGD.Core;
using TGD.Data;

namespace TGD.Combat
{
    public sealed class TurnManager
    {
        private readonly IList<Unit> _players;
        private readonly IList<Unit> _enemies;
        private readonly ICombatEventBus _eventBus;
        private readonly ICombatLogger _logger;
        private readonly ICombatTime _time;
        private readonly ISkillResolver _skillResolver;
        private readonly IDamageSystem _damageSystem;
        private readonly IResourceSystem _resourceSystem;
        private readonly IStatusSystem _statusSystem;
        private readonly ICooldownSystem _cooldownSystem;
        private readonly ISkillModSystem _skillModSystem;
        private readonly IMovementSystem _movementSystem;
        private readonly IAuraSystem _auraSystem;
        private readonly IScheduler _scheduler;
        private readonly RuntimeCtx _runtime;
        private readonly List<Unit> _allyBuffer = new();
        private readonly List<Unit> _enemyBuffer = new();
        private readonly Dictionary<Unit, Dictionary<string, int>> _dotHotRounds = new();

        private bool _turnShouldEnd;
        private int _roundIndex;

        // ★ 新增：对外事件（供 CombatLoop / 视图桥接订阅）
        public event Action<Unit> OnTurnBegan;
        public event Action<Unit> OnTurnEnded;

        public TurnManager(IList<Unit> players, IList<Unit> enemies,
            ICombatEventBus eventBus, ICombatLogger logger, ICombatTime time,
            ISkillResolver skillResolver, IDamageSystem damageSystem,
            IResourceSystem resourceSystem, IStatusSystem statusSystem,
            ICooldownSystem cooldownSystem, ISkillModSystem skillModSystem,
            IMovementSystem movementSystem, IAuraSystem auraSystem,
            IScheduler scheduler)
        {
            _players = players ?? Array.Empty<Unit>();
            _enemies = enemies ?? Array.Empty<Unit>();
            _eventBus = eventBus;
            _logger = logger;
            _time = time;
            _skillResolver = skillResolver;
            _damageSystem = damageSystem;
            _resourceSystem = resourceSystem;
            _statusSystem = statusSystem;
            _cooldownSystem = cooldownSystem;
            _skillModSystem = skillModSystem;
            _movementSystem = movementSystem;
            _auraSystem = auraSystem;
            _scheduler = scheduler;

            _runtime = new RuntimeCtx
            {
                Logger = logger,
                EventBus = eventBus,
                Time = time,
                SkillResolver = skillResolver,
                DamageSystem = damageSystem,
                ResourceSystem = resourceSystem,
                StatusSystem = statusSystem,
                CooldownSystem = cooldownSystem,
                SkillModSystem = skillModSystem,
                MovementSystem = movementSystem,
                AuraSystem = auraSystem,
                Scheduler = scheduler
            };
        }

        public Unit ActiveUnit { get; private set; }

        public IEnumerator RunLoop()
        {
            while (true)
            {
                foreach (var player in _players)
                {
                    if (player == null) continue;
                    yield return RunTurn(player, isPlayer: true);
                }

                foreach (var enemy in _enemies)
                {
                    if (enemy == null) continue;
                    yield return RunTurn(enemy, isPlayer: false);
                }

                _roundIndex++;
                yield return null;
            }
        }

        public void EndTurnEarly() => _turnShouldEnd = true;

        public bool ExecuteSkill(Unit caster, SkillDefinition skill, Unit primaryTarget, Unit secondaryTarget = null)
        {
            if (caster == null || skill == null) return false;
            if (ActiveUnit != caster) return false;

            if (caster.IsOnCooldown(skill))
            {
                _logger?.Log("SKILL_COOLDOWN", caster.UnitId, skill.skillID);
                return false;
            }
            if (!HasResources(caster, skill))
            {
                _logger?.Log("SKILL_RESOURCE_FAIL", caster.UnitId, skill.skillID);
                return false;
            }

            int timeCost = Mathf.Max(0, skill.timeCostSeconds);
            if (caster.RemainingTime < timeCost)
            {
                _logger?.Log("SKILL_TIME_FAIL", caster.UnitId, skill.skillID, caster.RemainingTime, timeCost);
                return false;
            }

            SpendResources(caster, skill);

            PrepareRuntime(caster, primaryTarget, secondaryTarget, skill);
            _logger?.Log("SKILL_CAST", caster.UnitId, skill.skillID, primaryTarget?.UnitId ?? "none");

            ActionSystem.Execute(caster, skill, _runtime);
            caster.SetCooldown(skill);

            caster.SpendTime(timeCost);
            _time?.Advance(timeCost);

            if (caster.RemainingTime <= 0)
                _turnShouldEnd = true;

            return true;
        }

        private IEnumerator RunTurn(Unit unit, bool isPlayer)
        {
            ActiveUnit = unit;
            _turnShouldEnd = false;

            unit.StartTurn();
            _eventBus?.EmitTurnBegin(unit);
            _logger?.Log("TURN_BEGIN", unit.UnitId, _roundIndex, unit.RemainingTime);
            OnTurnBegan?.Invoke(unit);     // ★ 新增：对外广播

            ProcessDotHot(unit);

            if (isPlayer)
            {
                while (!_turnShouldEnd)
                {
                    if (unit.RemainingTime <= 0) break;
                    yield return null;
                }
            }
            else
            {
                float wait = 1f;
                while (!_turnShouldEnd && wait > 0f)
                {
                    wait -= Time.deltaTime;
                    yield return null;
                }
                _turnShouldEnd = true;
            }

            FinishTurn(unit, isPlayer);
            ActiveUnit = null;
        }

        private void FinishTurn(Unit unit, bool isPlayer)
        {
            unit.EndTurn();
            _eventBus?.EmitTurnEnd(unit);
            _logger?.Log("TURN_END", unit.UnitId);
            OnTurnEnded?.Invoke(unit);     // ★ 新增：对外广播

            _cooldownSystem?.TickEndOfTurn();

            if (!isPlayer)
                ApplyEnemyRegeneration(unit);
        }

        private void ApplyEnemyRegeneration(Unit unit)
        {
            if (unit?.Stats == null) return;

            if (unit.Stats.HealthRegenPerTurn > 0)
                unit.Stats.HP = Mathf.Min(unit.Stats.MaxHP, unit.Stats.HP + unit.Stats.HealthRegenPerTurn);

            if (unit.Stats.ArmorRegenPerTurn > 0)
                unit.Stats.Armor = Mathf.Max(0, unit.Stats.Armor + unit.Stats.ArmorRegenPerTurn);

            unit.Stats.Clamp();
            _logger?.Log("ENEMY_REGEN", unit.UnitId, unit.Stats.HP, unit.Stats.Armor);
        }

        private void PrepareRuntime(Unit caster, Unit primaryTarget, Unit secondaryTarget, SkillDefinition skill)
        {
            _runtime.Caster = caster;
            _runtime.PrimaryTarget = primaryTarget;
            _runtime.SecondaryTarget = secondaryTarget;
            _runtime.Skill = skill;

            _allyBuffer.Clear();
            _enemyBuffer.Clear();

            CollectUnits(_players, caster.TeamId, _allyBuffer, includeTeam: true);
            CollectUnits(_enemies, caster.TeamId, _allyBuffer, includeTeam: true);
            CollectUnits(_players, caster.TeamId, _enemyBuffer, includeTeam: false);
            CollectUnits(_enemies, caster.TeamId, _enemyBuffer, includeTeam: false);

            _runtime.Allies = _allyBuffer.ToArray();
            _runtime.Enemies = _enemyBuffer.ToArray();
        }

        private static void CollectUnits(IEnumerable<Unit> source, int teamId, ICollection<Unit> destination, bool includeTeam)
        {
            if (source == null || destination == null) return;

            foreach (var unit in source)
            {
                if (unit == null) continue;
                if (includeTeam && unit.TeamId == teamId) destination.Add(unit);
                else if (!includeTeam && unit.TeamId != teamId) destination.Add(unit);
            }
        }

        private bool HasResources(Unit caster, SkillDefinition skill)
        {
            if (skill.costs == null || skill.costs.Count == 0) return true;

            foreach (var cost in skill.costs)
            {
                if (cost == null) continue;
                if (cost.resourceType == CostResourceType.Custom) continue;
                if (!ResourceUtility.TryGetAccessor(caster.Stats, cost.resourceType, out var accessor) || !accessor.IsValid)
                    continue;

                float amount = cost.ResolveAmount();
                if (cost.resourceType == CostResourceType.HP)
                {
                    if (accessor.Current < amount) return false;
                }
                else
                {
                    if (accessor.Current < amount) return false;
                }
            }
            return true;
        }

        private void SpendResources(Unit caster, SkillDefinition skill)
        {
            if (skill.costs == null) return;

            foreach (var cost in skill.costs)
            {
                if (cost == null) continue;
                if (cost.resourceType == CostResourceType.Custom) continue;
                if (!ResourceUtility.TryGetAccessor(caster.Stats, cost.resourceType, out var accessor) || !accessor.IsValid)
                    continue;

                int amount = Mathf.RoundToInt(cost.ResolveAmount());
                if (cost.resourceType == CostResourceType.HP)
                {
                    accessor.Current = Mathf.Max(1, accessor.Current - amount);
                }
                else
                {
                    accessor.Current = Mathf.Max(0, accessor.Current - amount);
                }
            }
            caster.Stats.Clamp();
        }

        private void ProcessDotHot(Unit unit)
        {
            if (unit?.Statuses == null || unit.Statuses.Count == 0) return;

            if (!_dotHotRounds.TryGetValue(unit, out var tracker))
            {
                tracker = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                _dotHotRounds[unit] = tracker;
            }

            var stillActive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var status in unit.Statuses)
            {
                if (status == null) continue;

                var statusSkill = status.StatusSkill ?? _skillResolver?.ResolveById(status.StatusSkillId);
                if (statusSkill == null || statusSkill.effects == null) continue;

                foreach (var effect in statusSkill.effects)
                {
                    if (effect == null) continue;

                    if (effect.dotHotOperation != DotHotOperation.TriggerDots &&
                        effect.dotHotOperation != DotHotOperation.TriggerHots)
                        continue;

                    stillActive.Add(status.StatusSkillId);
                    if (!ShouldTriggerDotHot(tracker, status.StatusSkillId)) continue;

                    ApplyDotHot(status, effect);
                    tracker[status.StatusSkillId] = _roundIndex;
                }
            }

            var toRemove = tracker.Keys.Where(id => !stillActive.Contains(id)).ToList();
            foreach (var key in toRemove) tracker.Remove(key);
        }

        private bool ShouldTriggerDotHot(Dictionary<string, int> tracker, string statusId)
        {
            if (string.IsNullOrWhiteSpace(statusId)) return false;
            return !tracker.TryGetValue(statusId, out var lastRound) || lastRound < _roundIndex;
        }

        private void ApplyDotHot(StatusInstance status, EffectDefinition effect)
        {
            var target = status.Target;
            if (target == null) return;

            var source = status.Source ?? target;
            PrepareRuntime(source, target, null, status.StatusSkill ?? _skillResolver?.ResolveById(status.StatusSkillId));

            float baseAmount = effect.value;
            if (status.StackCount > 1) baseAmount *= status.StackCount;

            EffectOp op;
            if (effect.dotHotOperation == DotHotOperation.TriggerHots)
            {
                op = new HealOp { Source = source, Target = target, Amount = baseAmount, CanCrit = effect.canCrit };
            }
            else
            {
                op = new DealDamageOp { Source = source, Target = target, Amount = baseAmount, CanCrit = effect.canCrit, School = effect.damageSchool };
            }

            EffectOpRunner.Run(new[] { op }, _runtime);
            _logger?.Log(effect.dotHotOperation == DotHotOperation.TriggerHots ? "HOT_TICK" : "DOT_TICK",
                target.UnitId, baseAmount, status.StatusSkillId);
        }
    }
}
