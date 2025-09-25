using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TGD.Core;
using TGD.Data;

namespace TGD.Combat
{
    /// <summary>Combat 入口（不引用 UI/Level）。对外暴露事件。</summary>
    public class CombatLoop : MonoBehaviour
    {
        [Tooltip("玩家队伍（最多4人）")]
        public List<Unit> playerParty = new();

        [Tooltip("敌人队伍")]
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

        // —— 事件：供 UI/Level 订阅 —— //
        public event Action<Unit> OnTurnBegan;
        public event Action<Unit> OnTurnEnded;

        // 飘字请求：UI 层订阅后渲染
        public enum DamageHint { Normal, Crit, Heal }
        public event Action<Unit, int, DamageHint> OnDamageNumberRequested;

        // 便捷单例（可选）
        public static CombatLoop Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            InitializeSystems();
            HookTurnCallbacks();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

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

            PopulateUnits(playerParty, teamId: 0);
            PopulateUnits(enemyParty, teamId: 1);

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

            _turnManager = new TurnManager(
                playerParty, enemyParty, _eventBus, _logger, _combatTime,
                _skillResolver, damageSystem, resourceSystem, _statusSystem, _cooldownSystem,
                skillModSystem, movementSystem, auraSystem, scheduler);
        }

        private void PopulateUnits(IEnumerable<Unit> units, int teamId)
        {
            if (units == null) return;

            foreach (var unit in units)
            {
                if (unit == null) continue;

                unit.TeamId = teamId;                // 明确友敌
                unit.Stats ??= new Stats();
                unit.Stats.Clamp();

                unit.Skills ??= new List<SkillDefinition>();
                if (!string.IsNullOrWhiteSpace(unit.ClassId))
                {
                    var skills = SkillDatabase.GetSkillsForClass(unit.ClassId);
                    unit.Skills.Clear();
                    foreach (var s in skills) unit.Skills.Add(s); // 共享配置即可（冷却由系统管）
                }
            }
        }

        public bool ExecuteSkill(Unit caster, string skillId, Unit primaryTarget)
        {
            if (_turnManager == null || caster == null || string.IsNullOrWhiteSpace(skillId))
                return false;

            var skill = _skillResolver.ResolveById(skillId);
            if (skill == null) return false;

            return _turnManager.ExecuteSkill(caster, skill, primaryTarget);
        }

        public void EndActiveTurn() => _turnManager?.EndTurnEarly();
        public Unit GetActiveUnit() => _turnManager?.ActiveUnit;

        // —— TurnManager 事件转发（如果你 TurnManager 暴露事件，就在此订阅；否则可在 Update 里轮询） —— //
        private void HookTurnCallbacks()
        {
            // 若 TurnManager 没有事件，你也可以用轮询：
            // StartCoroutine(PollActiveUnit());
            if (_turnManager == null) return;

            // 示例：你的 TurnManager 若有这两个事件，请在构造或初始化后绑定：
            _turnManager.OnTurnBegan += u => OnTurnBegan?.Invoke(u);
            _turnManager.OnTurnEnded += u => OnTurnEnded?.Invoke(u);
        }

        // Combat 层只“请求”飘字，UI/Level 来负责渲染
        public void RequestDamageNumber(Unit target, int amount, DamageHint hint = DamageHint.Normal)
            => OnDamageNumberRequested?.Invoke(target, amount, hint);

       
        public void ReinitializeAndStart()
        {
            StopAllCoroutines();
            InitializeSystems();
            if (autoStart && _turnManager != null)
                StartCoroutine(_turnManager.RunLoop());
        }
    }
}
