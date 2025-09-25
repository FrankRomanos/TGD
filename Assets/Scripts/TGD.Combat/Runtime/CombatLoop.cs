using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TGD.Core;
using TGD.Data;
using TGD.Grid;



namespace TGD.Combat
{
    public class CombatLoop : MonoBehaviour
    {
        
        public static CombatLoop Instance { get; private set; }

        [Tooltip("玩家队伍")]
        public List<Unit> playerParty = new();

        [Tooltip("敌人队伍")]
        public List<Unit> enemyParty = new();

        [Tooltip("可选：战斗日志（若空会自动创建临时实例）")]
        public CombatLog combatLog;

        public bool autoStart = false;   // 用 PartyBootstrapper 启动即可

        [Header("Grid")]
        public HexGridAuthoring battlefieldGrid;

        public ICombatEventBus EventBus => _eventBus;
        public HexGridAuthoring ActiveGrid => _gridAuthoring;
        public HexGridMap<Unit> GridMap => _gridMap;
        // CombatLoop.cs（属于 TGD.Combat）



        // —— 事件给视图层（桥/飘字）——
        public event Action<Unit> OnTurnBegan;
        public event Action<Unit> OnTurnEnded;
        public enum DamageHint { Normal, Crit, Heal }
        public event Action<Unit, int, DamageHint> OnDamageNumberRequested;

        // —— 内部系统 —— 
        private TurnManager _turnManager;
        private ICombatEventBus _eventBus;
        private ICombatTime _combatTime;
        private ICombatLogger _logger;
        private ISkillResolver _skillResolver;
        private IStatusSystem _statusSystem;
        private ICooldownSystem _cooldownSystem;
        private HexGridMap<Unit> _gridMap;
        private HexGridAuthoring _gridAuthoring;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            if (autoStart) ReinitializeAndStart();
        }

        // —— 对外：给引导器调用，一步初始化并开跑 —— //
        public void ReinitializeAndStart()
        {
            StopAllCoroutines();
            InitializeSystems();
            if (_turnManager != null)
                StartCoroutine(_turnManager.RunLoop());
        }

        // —— 让桥兜底轮询调用 —— //
        public Unit GetActiveUnit() => _turnManager?.ActiveUnit;

        // —— 在合适位置让 TurnManager 调用（它若没有事件，也可在需要时手动调用）—— //
        public void RaiseTurnBegan(Unit u) => OnTurnBegan?.Invoke(u);
        public void RaiseTurnEnded(Unit u) => OnTurnEnded?.Invoke(u);
        public void RaiseDamageNumber(Unit target, int amount, DamageHint hint) =>
            OnDamageNumberRequested?.Invoke(target, amount, hint);

        // ================== 你原来的初始化逻辑 ==================
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
            InitializeGrid(allUnits);

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
                playerParty, enemyParty,
                _eventBus, _logger, _combatTime,
                _skillResolver, damageSystem, resourceSystem,
                _statusSystem, _cooldownSystem,
                skillModSystem, movementSystem, auraSystem, scheduler
            );

            _turnManager.SetGrid(_gridMap);

            // 如果你的 TurnManager 有事件，就转发给视图层；没有也没关系，桥会轮询
            try
            {
                _turnManager.OnTurnBegan += u => OnTurnBegan?.Invoke(u);
                _turnManager.OnTurnEnded += u => OnTurnEnded?.Invoke(u);
            }
            catch { /* 没有事件就忽略 */ }
        }

        private void PopulateUnits(IEnumerable<Unit> units)
        {
            if (units == null) return;

            foreach (var unit in units)
            {
                if (unit == null) continue;

                unit.Stats ??= new Stats();
                ClassResourceCatalog.ApplyDefaults(unit);
                unit.Stats.Clamp();

                unit.Skills ??= new List<SkillDefinition>();

                if (!string.IsNullOrWhiteSpace(unit.ClassId))
                {
                    var skills = SkillDatabase.GetSkillsForClass(unit.ClassId);
                    unit.Skills.Clear();
                    foreach (var skill in skills)
                        unit.Skills.Add(skill);
                    unit.NotifySkillsChanged();
                }
            }
        }

        private void InitializeGrid(IReadOnlyList<Unit> units)
        {
            _gridAuthoring = ResolveGridAuthoring();
            if (_gridAuthoring == null && battlefieldGrid)
                _gridAuthoring = battlefieldGrid;
            else if (_gridAuthoring != null)
                battlefieldGrid = _gridAuthoring;

            var layout = _gridAuthoring?.Layout;
            if (layout == null)
            {
                _gridMap = null;
                return;
            }

            _gridMap = new HexGridMap<Unit>(layout);
            if (units == null)
                return;

            foreach (var unit in units)
            {
                if (unit == null)
                    continue;

                var coord = unit.Position;
                if (!layout.Contains(coord))
                {
                    if (!TryResolveActorCoordinate(unit, layout, out coord))
                        coord = layout.ClampToBounds(HexCoord.Zero, coord);
                }
                unit.Position = coord;
                _gridMap.SetPosition(unit, coord);
            }
        }

        private bool TryResolveActorCoordinate(Unit unit, HexGridLayout layout, out HexCoord coord)
        {
            coord = default;
            var bridge = CombatViewBridge.Instance;
            if (bridge != null && bridge.TryGetActor(unit, out var actor) && actor)
            {
                var grid = actor.ResolveGrid();
                var targetLayout = grid?.Layout ?? layout;
                coord = targetLayout.GetCoordinate(actor.transform.position);
                if (!layout.Contains(coord))
                    coord = layout.ClampToBounds(HexCoord.Zero, coord);
                return true;
            }

            return false;
        }

        private HexGridAuthoring ResolveGridAuthoring()
        {
            if (battlefieldGrid)
            {
                if (battlefieldGrid.Layout == null)
                    battlefieldGrid.Rebuild();
                if (battlefieldGrid.Layout != null)
                    return battlefieldGrid;
            }
            var bridge = CombatViewBridge.Instance;
            if (bridge != null)
            {
                foreach (var actor in bridge.EnumerateActors())
                {
                    var candidate = actor?.ResolveGrid();
                    if (candidate && candidate.Layout != null)
                        return candidate;
                }
            }

#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<HexGridAuthoring>(FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<HexGridAuthoring>();
#endif
        }

        // —— UI按钮可调用 —— //
        public bool ExecuteSkill(Unit caster, SkillDefinition skill, Unit primaryTarget)
        {
            if (_turnManager == null || caster == null || skill == null)
                return false;

            return _turnManager.ExecuteSkill(caster, skill, primaryTarget);
        }

        public bool ExecuteSkill(Unit caster, string skillId, Unit primaryTarget)
        {
            if (_turnManager == null || caster == null || string.IsNullOrWhiteSpace(skillId))
                return false;

            var skill = _skillResolver.ResolveById(skillId);
            return ExecuteSkill(caster, skill, primaryTarget);
        }

        public void EndActiveTurn() => _turnManager?.EndTurnEarly();
    }
}
