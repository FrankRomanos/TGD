using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TGD.Core;
using TGD.Data;

namespace TGD.Combat
{
    public class CombatLoop : MonoBehaviour
    {
        [Tooltip("玩家队伍（最多4人）")]
        public List<Unit> playerParty = new();

        [Tooltip("敌人队伍（当前默认木桩）")]
        public List<Unit> enemyParty = new();

        [Tooltip("可选的战斗日志存储对象（如果为空会自动创建临时实例）")]
        public CombatLog combatLog;

        public bool autoStart = true;

        private TurnManager _turnManager;
        private ICombatEventBus _eventBus;
        private ICombatTime _combatTime;
        private ICombatLogger _logger;
        private ISkillResolver _skillResolver;
        private IStatusSystem _statusSystem;
        private ICooldownSystem _cooldownSystem;

        private void Awake()
        {
            InitializeSystems();
        }

        private void Start()
        {
            if (autoStart && _turnManager != null)
                StartCoroutine(_turnManager.RunLoop());
        }

        private void InitializeSystems()
        {
            SkillDatabase.EnsureLoaded();

            combatLog ??= ScriptableObject.CreateInstance<CombatLog>();
            combatLog.Clear();
            _logger = combatLog;

            _eventBus = new CombatEventBus();
            _combatTime = new CombatTime();

            PopulateUnits(playerParty);
            PopulateUnits(enemyParty);

            var allUnits = new List<Unit>();
            if (playerParty != null) allUnits.AddRange(playerParty.Where(u => u != null));
            if (enemyParty != null) allUnits.AddRange(enemyParty.Where(u => u != null));

            var movementSystem = new MovementSystem(_eventBus, _logger);
            _statusSystem = new StatusSystem(_eventBus, _combatTime, _logger);
            var damageSystem = new DamageSystem(_logger, _eventBus, _combatTime);
            var resourceSystem = new ResourceSystem(_logger);
            var skillModSystem = new SkillModSystem();
            var auraSystem = new AuraSystem(_logger);
            var scheduler = new Scheduler();
            _cooldownSystem = new CooldownSystem(allUnits, _statusSystem, _logger);

            _skillResolver = new DefaultSkillResolver(SkillDatabase.GetAllSkills());

            _turnManager = new TurnManager(playerParty, enemyParty, _eventBus, _logger, _combatTime,
                _skillResolver, damageSystem, resourceSystem, _statusSystem, _cooldownSystem,
                skillModSystem, movementSystem, auraSystem, scheduler);
        }

        private void PopulateUnits(IEnumerable<Unit> units)
        {
            if (units == null)
                return;

            foreach (var unit in units)
            {
                if (unit == null)
                    continue;

                unit.Stats ??= new Stats();
                unit.Stats.Clamp();

                unit.Skills ??= new List<SkillDefinition>();

                if (!string.IsNullOrWhiteSpace(unit.ClassId))
                {
                    var skills = SkillDatabase.GetSkillsForClass(unit.ClassId);
                    unit.Skills.Clear();
                    foreach (var skill in skills)
                        unit.Skills.Add(skill);
                }
            }
        }

        public bool ExecuteSkill(Unit caster, string skillId, Unit primaryTarget)
        {
            if (_turnManager == null || caster == null || string.IsNullOrWhiteSpace(skillId))
                return false;

            var skill = _skillResolver.ResolveById(skillId);
            if (skill == null)
                return false;

            return _turnManager.ExecuteSkill(caster, skill, primaryTarget);
        }

        public void EndActiveTurn()
        {
            _turnManager?.EndTurnEarly();
        }

        public Unit GetActiveUnit() => _turnManager?.ActiveUnit;
    }
}
