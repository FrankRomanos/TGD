using System;
using System.Collections.Generic;
using TGD.CombatV2;
using TGD.CombatV2.Integration;
using TGD.CombatV2.Targeting;
using TGD.CoreV2;
using TGD.DataV2;
using TGD.HexBoard;
using UnityEngine;

namespace TGD.LevelV2
{
    [DisallowMultipleComponent]
    public sealed class UnitFactory : MonoBehaviour
    {
        [Header("Deps (assign in Inspector)")]
        public TurnManagerV2 turnManager;
        public CombatActionManagerV2 cam;
        [SerializeField]
        HexOccupancyService occupancyService;
        [SerializeField]
        FootprintShape defaultFootprint;
        public Transform unitRoot;

        [Header("Prefab Source")]
        [Tooltip("Optional default prefab to instantiate when spawning units.")]
        public GameObject defaultPrefab;

        [Header("Data")]
        [Tooltip("Optional catalog used to validate skills when composing units.")]
        public SkillIndex skillIndex;

        [Header("Battle Control")]
        [Tooltip("Automatically call StartBattle after at least one unit is spawned.")]
        public bool autoStartBattle;

        readonly Dictionary<Unit, SpawnRecord> _spawned = new();
        readonly List<Unit> _friendlies = new();
        readonly List<Unit> _enemies = new();
        readonly HashSet<string> _usedIds = new();
        bool _battleStarted;
        DefaultTargetValidator _sharedValidator;
        bool _loggedMissingSkillIndex;

        sealed class SpawnRecord
        {
            public GameObject gameObject;
            public UnitRuntimeContext context;
            public CooldownHubV2 cooldownHub;
            public UnitGridAdapter gridAdapter;
        }

        public IReadOnlyList<Unit> FriendlyUnits => _friendlies;
        public IReadOnlyList<Unit> EnemyUnits => _enemies;
        public IEnumerable<Unit> AllUnits => _spawned.Keys;

        public Unit Spawn(UnitBlueprint blueprint, UnitFaction faction, Hex spawnHex)
        {
            if (blueprint == null)
            {
                Debug.LogError("[Factory] Spawn failed: blueprint is null.", this);
                return null;
            }

            var resolvedSkillIndex = ResolveSkillIndex();
            var final = UnitComposeService.Compose(blueprint, resolvedSkillIndex);
            final.faction = faction;

            // 优先使用蓝图上的 prefab，其次 defaultPrefab，最后 Resources 兜底
            var prefab = ResolvePrefab(preferred: blueprint.basePrefab);
            return SpawnFromFinal(final, spawnHex, prefab);
        }


        public Unit SpawnFromFinal(FinalUnitConfig final, Hex spawnHex, GameObject prefab)
        {
            if (final == null)
            {
                Debug.LogError("[Factory] Spawn failed: final config is null.", this);
                return null;
            }

            if (prefab == null)
            {
                Debug.LogError("[Factory] Spawn failed: missing unit prefab.", this);
                return null;
            }

            var parent = unitRoot != null ? unitRoot : transform;
            var go = Instantiate(prefab, parent);

            var context = EnsureContext(go);
            var cooldownHub = EnsureCooldownHub(go, context);
            ApplyStats(context, final.stats);
            InitializeCooldowns(cooldownHub, final.abilities);

            string unitId = ReserveUnitId(final.unitId, final.displayName);
            var unit = new Unit(unitId, spawnHex, Facing4.PlusQ)
            {
                Position = spawnHex
            };

            if (context != null && context.boundUnit == null)
                context.boundUnit = unit;

            go.name = $"{ResolveDisplayName(final, unitId)} ({final.faction})";
            PlaceTransform(go.transform, spawnHex);

            var adapter = EnsureGridAdapter(go, unit);
            RegisterTurnSystems(unit, context, cooldownHub, final.faction);
            var resolvedSkillIndex = ResolveSkillIndex();
            var resolvedTurnManager = WireActionComponents(go, context, cooldownHub, unit, resolvedSkillIndex);

            FinalizeOccupancyChain(go, context, unit, final.faction, adapter, resolvedTurnManager);

            TrackUnit(unit, final.faction, go, context, cooldownHub, adapter, final.avatar);

            Debug.Log($"[Factory] Spawn {ResolveDisplayName(final, unitId)} ({final.faction}) at {spawnHex}", this);

            var availabilities = UnitActionBinder.Bind(go, context, final.abilities);
            ApplyAbilityLoadout(go, context, availabilities, cam, resolvedSkillIndex);

            MaybeAutoStartBattle();
            return unit;
        }

        public void Despawn(Unit unit)
        {
            if (unit == null)
                return;

            if (!_spawned.TryGetValue(unit, out var record) || record == null)
                return;

            var occ = ResolveOccupancy();
            if (occ != null && record.gridAdapter != null)
                occ.Remove(record.gridAdapter);

            if (turnManager != null)
            {
                if (record.context != null)
                    record.context.cooldownHub = null;
            }

            if (record.gameObject != null)
                Destroy(record.gameObject);

            _spawned.Remove(unit);
            _friendlies.Remove(unit);
            _enemies.Remove(unit);
            _usedIds.Remove(unit.Id);

            if (_spawned.Count == 0)
                _battleStarted = false;
        }

        public void StartBattle()
        {
            if (turnManager == null)
            {
                Debug.LogWarning("[Factory] Cannot start battle without TurnManager.", this);
                return;
            }

            var players = new List<Unit>(_friendlies);
            var enemies = new List<Unit>(_enemies);
            turnManager.StartBattle(players, enemies);
            _battleStarted = true;
        }

        void MaybeAutoStartBattle()
        {
            if (!autoStartBattle || _battleStarted)
                return;
            if (turnManager == null)
                return;
            if (_friendlies.Count == 0 && _enemies.Count == 0)
                return;
            StartBattle();
        }

        GameObject ResolvePrefab(GameObject preferred = null)
        {
            if (preferred != null) return preferred;
            if (defaultPrefab != null) return defaultPrefab;

            const string resourcePath = "Units/DefaultUnit";
            var loaded = Resources.Load<GameObject>(resourcePath);
            if (loaded != null)
            {
                defaultPrefab = loaded;
                return defaultPrefab;
            }
            return null;
        }

        static UnitRuntimeContext EnsureContext(GameObject go)
        {
            var context = go.GetComponent<UnitRuntimeContext>();
            if (context == null)
                context = go.AddComponent<UnitRuntimeContext>();
            if (context.stats == null)
                context.stats = new StatsV2();
            return context;
        }

        static CooldownHubV2 EnsureCooldownHub(GameObject go, UnitRuntimeContext context)
        {
            var hub = go.GetComponent<CooldownHubV2>();
            if (hub == null)
                hub = go.AddComponent<CooldownHubV2>();
            if (context != null)
                context.cooldownHub = hub;
            return hub;
        }

        static void ApplyStats(UnitRuntimeContext context, StatsV2 source)
        {
            if (context == null)
                return;

            if (context.stats == null)
                context.stats = new StatsV2();

            if (source == null)
                source = new StatsV2();

            context.stats.ApplyInit(source);
        }

        static void InitializeCooldowns(CooldownHubV2 hub, List<FinalUnitConfig.LearnedAbility> abilities)
        {
            if (hub == null)
                return;

            hub.secStore = new CooldownStoreSecV2();
            if (abilities == null)
                return;

            foreach (var ability in abilities)
            {
                if (string.IsNullOrWhiteSpace(ability.skillId))
                    continue;
                hub.secStore.StartSeconds(ability.skillId.Trim(), Mathf.Max(0, ability.initialCooldownSeconds));
            }
        }

        string ReserveUnitId(string preferred, string fallback)
        {
            string baseId = !string.IsNullOrWhiteSpace(preferred)
                ? preferred.Trim()
                : (!string.IsNullOrWhiteSpace(fallback) ? fallback.Trim() : "Unit");

            string candidate = baseId;
            int index = 1;
            while (_usedIds.Contains(candidate))
            {
                candidate = $"{baseId}_{index}";
                index++;
            }

            _usedIds.Add(candidate);
            return candidate;
        }

        static string ResolveDisplayName(FinalUnitConfig final, string unitId)
        {
            if (final != null && !string.IsNullOrWhiteSpace(final.displayName))
                return final.displayName;
            return unitId;
        }

        void PlaceTransform(Transform target, Hex spawnHex)
        {
            if (target == null)
                return;

            Vector3 position;
            var space = HexSpace.Instance;
            if (space != null)
            {
                position = space.HexToWorld(spawnHex);
            }
            else if (occupancyService != null && occupancyService.authoring != null && occupancyService.authoring.Layout != null)
            {
                position = occupancyService.authoring.Layout.World(spawnHex, occupancyService.authoring.y);
            }
            else
            {
                position = new Vector3(spawnHex.q, 0f, spawnHex.r);
            }

            target.position = position;
        }

        HexOccupancy ResolveOccupancy()
        {
            HexOccupancyService service = occupancyService;
            if (service == null && turnManager != null)
                service = turnManager.occupancyService;
            return service != null ? service.Get() : null;
        }

        void RegisterTurnSystems(Unit unit, UnitRuntimeContext context, CooldownHubV2 hub, UnitFaction faction)
        {
            if (turnManager != null && unit != null)
                turnManager.Bind(unit, context);

            var list = faction == UnitFaction.Friendly ? _friendlies : _enemies;
            if (!list.Contains(unit))
                list.Add(unit);
        }

        TurnManagerV2 WireActionComponents(
            GameObject go,
            UnitRuntimeContext context,
            CooldownHubV2 hub,
            Unit unit,
            SkillIndex resolvedSkillIndex)
        {
            if (go == null)
                return turnManager;

            var resolvedTurnManager = turnManager ?? FindOne<TurnManagerV2>();
            var resolvedCam = cam ?? FindOne<CombatActionManagerV2>();

            if (resolvedCam != null && resolvedCam.rulebook != null && resolvedSkillIndex != null)
            {
                if (resolvedCam.rulebook.includeSkillIndexDerivedLinks && resolvedCam.rulebook.skillIndex == null)
                {
                    resolvedCam.rulebook.skillIndex = resolvedSkillIndex;
                }
            }

            var resolvedAuthoring = occupancyService != null ? occupancyService.authoring : null;
            if (resolvedAuthoring == null && resolvedCam != null)
                resolvedAuthoring = resolvedCam.authoring;
            if (resolvedAuthoring == null)
                resolvedAuthoring = FindOne<HexBoardAuthoringLite>();

            var resolvedTiler = (resolvedCam != null ? resolvedCam.tiler : null)
                                ?? (resolvedAuthoring != null ? resolvedAuthoring.GetComponentInChildren<HexBoardTiler>(true) : null)
                                ?? FindOne<HexBoardTiler>();

            var resolvedValidator = (resolvedCam != null ? resolvedCam.GetComponentInChildren<DefaultTargetValidator>(true) : null)
                                    ?? (resolvedAuthoring != null ? resolvedAuthoring.GetComponentInChildren<DefaultTargetValidator>(true) : null)
                                    ?? FindOne<DefaultTargetValidator>();
            if (resolvedValidator != null && _sharedValidator == null)
                _sharedValidator = resolvedValidator;

            var resolvedOccupancy = occupancyService
                                    ?? (resolvedTurnManager != null ? resolvedTurnManager.occupancyService : null)
                                    ?? FindOne<HexOccupancyService>();

            var ownerBridge = context ? context.GetComponentInParent<PlayerOccupancyBridge>(true) : null;
            var view = go.GetComponentInChildren<Animator>(true)?.transform
                    ?? go.GetComponentInChildren<SkinnedMeshRenderer>(true)?.transform
                    ?? go.transform;

            var movers = go.GetComponentsInChildren<HexClickMover>(true);
            foreach (var mover in movers)
            {
                if (mover == null)
                    continue;

                mover.ctx = context;
                mover.AttachTurnManager(resolvedTurnManager);
                if (!mover.authoring)
                    mover.authoring = resolvedAuthoring;
                if (!mover.tiler)
                    mover.tiler = resolvedTiler;
                if (!mover.targetValidator)
                    mover.targetValidator = resolvedValidator;
                if (!mover.occupancyService)
                    mover.occupancyService = resolvedOccupancy;
                if (ownerBridge != null)
                    mover.bridgeOverride = ownerBridge;
                mover.viewOverride = view;
                mover.driver = null;
            }

            var moveCosts = go.GetComponentsInChildren<MoveCostServiceV2Adapter>(true);
            foreach (var moveCost in moveCosts)
            {
                if (moveCost == null)
                    continue;

                if (moveCost.stats == null)
                    moveCost.stats = context != null ? context.stats : null;
                if (moveCost.cooldownHub == null)
                    moveCost.cooldownHub = hub;
                moveCost.ctx = context;
                moveCost.turnManager = turnManager;
            }

            var attacks = go.GetComponentsInChildren<AttackControllerV2>(true);
            foreach (var attack in attacks)
            {
                if (attack == null)
                    continue;

                attack.ctx = context;
                attack.turnManager = resolvedTurnManager;
                attack.AttachTurnManager(resolvedTurnManager);
                attack.driver = null;
                if (!attack.authoring)
                    attack.authoring = resolvedAuthoring;
                if (!attack.tiler)
                    attack.tiler = resolvedTiler;
                if (!attack.targetValidator)
                    attack.targetValidator = resolvedValidator;
                if (!attack.occupancyService)
                    attack.occupancyService = resolvedOccupancy;

                if (ownerBridge != null)
                    attack.bridgeOverride = ownerBridge;
                attack.viewOverride = view;
            }

            var attackAnimDrivers = go.GetComponentsInChildren<AttackAnimDriver>(true);
            foreach (var animDriver in attackAnimDrivers)
            {
                if (animDriver == null)
                    continue;

                if (animDriver.ctx == null)
                    animDriver.ctx = context;
                if (unit != null)
                    animDriver.BindUnit(unit);
            }

            var attackMoveListeners = go.GetComponentsInChildren<AttackMoveAnimListener>(true);
            foreach (var listener in attackMoveListeners)
            {
                if (listener == null)
                    continue;

                if (listener.ctx == null)
                    listener.ctx = context;
            }

            var chainActions = go.GetComponentsInChildren<ChainActionBase>(true);
            foreach (var chain in chainActions)
            {
                if (chain == null)
                    continue;

                chain.BindContext(context, resolvedTurnManager);
                if (chain.targetValidator == null)
                    chain.targetValidator = resolvedValidator;
                if (chain.tiler == null)
                    chain.tiler = resolvedTiler;
                if (chain.driver != null)
                    chain.driver = null;
            }

            var unitMoveListeners = go.GetComponentsInChildren<UnitMoveAnimListener>(true);
            foreach (var listener in unitMoveListeners)
            {
                if (listener == null)
                    continue;

                if (listener.ctx == null)
                    listener.ctx = context;
                if (listener.mover == null)
                    listener.mover = listener.GetComponent<HexClickMover>() ?? listener.GetComponentInParent<HexClickMover>(true);
            }

            var autoDrivers = go.GetComponentsInChildren<TestEnemyAutoActionDriver>(true);
            foreach (var driver in autoDrivers)
            {
                if (driver == null)
                    continue;

                if (cam != null)
                    driver.actionManager = cam;
                driver.turnManager = turnManager;
            }

            var statuses = go.GetComponentsInChildren<MoveRateStatusRuntime>(true);
            foreach (var status in statuses)
            {
                if (status == null)
                    continue;
                status.turnManager = turnManager;
                if (turnManager != null)
                    status.AttachTurnManager(turnManager);
            }

            return resolvedTurnManager;
        }

        SkillIndex ResolveSkillIndex()
        {
            if (skillIndex != null)
                return skillIndex;

            const string resourcePath = "Units/Blueprints/SkillIndex";
            var loaded = Resources.Load<SkillIndex>(resourcePath);
            if (loaded != null)
            {
                skillIndex = loaded;
                _loggedMissingSkillIndex = false;
                return skillIndex;
            }

            if (!_loggedMissingSkillIndex)
            {
                Debug.LogWarning($"[Factory] SkillIndex not assigned and default resource '{resourcePath}' missing.", this);
                _loggedMissingSkillIndex = true;
            }

            return null;
        }

        HexOccupancyService ResolveOccupancyService()
        {
            if (occupancyService != null)
                return occupancyService;
            if (turnManager != null && turnManager.occupancyService != null)
                return turnManager.occupancyService;
            return FindOne<HexOccupancyService>();
        }

        static UnitGridAdapter EnsureGridAdapter(GameObject go, Unit unit)
        {
            if (go == null)
                return null;

            var adapter = go.GetComponent<UnitGridAdapter>() ?? go.AddComponent<UnitGridAdapter>();
            if (unit != null)
                adapter.Unit = unit;
            return adapter;
        }

        void FinalizeOccupancyChain(GameObject go, UnitRuntimeContext context, Unit unit, UnitFaction faction, UnitGridAdapter adapter, TurnManagerV2 resolvedTurnManager)
        {
            if (go == null)
                return;

            var occSvc = ResolveOccupancyService();
            if (occSvc != null)
                Debug.Log($"[Factory] OccSvc instance={occSvc.GetInstanceID()} for {unit?.Id}", this);

            var bridge = go.GetComponent<PlayerOccupancyBridge>() ?? go.AddComponent<PlayerOccupancyBridge>();
            if (occupancyService == null && occSvc != null)
                occupancyService = occSvc;

            bridge.occupancyService = occSvc;
            if (bridge.overrideFootprint == null && defaultFootprint != null)
                bridge.overrideFootprint = defaultFootprint;

            foreach (var mover in go.GetComponentsInChildren<HexClickMover>(true))
            {
                if (mover != null)
                {
                    mover.occupancyService = occSvc;
                    if (mover.bridgeOverride == null)
                        mover.bridgeOverride = bridge;
                }
            }

            foreach (var attack in go.GetComponentsInChildren<AttackControllerV2>(true))
            {
                if (attack != null)
                {
                    attack.occupancyService = occSvc;
                    if (attack.bridgeOverride == null)
                        attack.bridgeOverride = bridge;
                }
            }

            var bound = context != null && context.boundUnit != null ? context.boundUnit : unit;
            if (adapter == null)
                adapter = EnsureGridAdapter(go, bound);
            else if (adapter.Unit == null && bound != null)
                adapter.Unit = bound;

            var tm = resolvedTurnManager != null ? resolvedTurnManager : turnManager;

            if (tm != null && bound != null)
            {
                bool isFriendly = faction == UnitFaction.Friendly;
                tm.RegisterSpawn(bound, isFriendly);
                bool isEnemy = tm.IsEnemyUnit(bound);
                bool isPlayer = tm.IsPlayerUnit(bound);
                Debug.Log($"[Factory] TM roster {bound.Id}: player={isPlayer} enemy={isEnemy}", this);
            }

            if (!bridge.EnsurePlacedNow())
                Debug.LogWarning($"[Factory] Failed to place {unit?.Id ?? go.name} on occupancy grid.", this);

            EnsureDefaultValidatorInjected(occSvc, tm);
        }

        static void ApplyAbilityLoadout(
            GameObject go,
            UnitRuntimeContext context,
            IReadOnlyList<UnitActionBinder.ActionAvailability> availabilities,
            CombatActionManagerV2 cam,
            SkillIndex skillIndex)
        {
            if (go == null)
                return;

            var granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unlocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AssignSkillDefinitions(go, availabilities, skillIndex);

            if (availabilities != null)
            {
                for (int i = 0; i < availabilities.Count; i++)
                {
                    var entry = availabilities[i];
                    var id = NormalizeSkillId(entry.skillId);
                    if (string.IsNullOrEmpty(id))
                        continue;

                    granted.Add(id);
                    if (entry.unlocked)
                        unlocked.Add(id);
                }
            }

            context?.SetGrantedActions(granted);

            var behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var behaviour in behaviours)
            {
                if (behaviour is IActionToolV2 tool)
                {
                    var id = NormalizeSkillId(tool.Id);
                    bool enable = !string.IsNullOrEmpty(id) && unlocked.Contains(id);
                    behaviour.enabled = enable;

                    if (cam != null)
                    {
                        if (!string.IsNullOrEmpty(id) && granted.Contains(id))
                            cam.RegisterTool(tool);
                        else
                            cam.UnregisterTool(tool);
                    }
                }
            }
        }

        static void AssignSkillDefinitions(
            GameObject go,
            IReadOnlyList<UnitActionBinder.ActionAvailability> availabilities,
            SkillIndex skillIndex)
        {
            if (go == null)
                return;

            var tools = go.GetComponentsInChildren<SkillDefinitionActionTool>(true);
            if (tools == null || tools.Length == 0)
                return;

            if (availabilities == null || availabilities.Count == 0)
                return;

            var map = new Dictionary<string, SkillDefinitionActionTool>(StringComparer.OrdinalIgnoreCase);
            var pool = new List<SkillDefinitionActionTool>();
            var poolSet = new HashSet<SkillDefinitionActionTool>();

            void AddToPool(SkillDefinitionActionTool candidate)
            {
                if (candidate != null && poolSet.Add(candidate))
                    pool.Add(candidate);
            }

            void RemoveFromPool(SkillDefinitionActionTool candidate)
            {
                if (candidate != null && poolSet.Remove(candidate))
                    pool.Remove(candidate);
            }

            for (int i = 0; i < tools.Length; i++)
            {
                var tool = tools[i];
                if (tool == null)
                    continue;

                var definitionId = NormalizeSkillId(tool.Definition != null ? tool.Definition.Id : null);
                if (!string.IsNullOrEmpty(definitionId) && !map.ContainsKey(definitionId))
                    map[definitionId] = tool;

                var explicitId = NormalizeSkillId(tool.Id);
                if (!string.IsNullOrEmpty(explicitId) && !map.ContainsKey(explicitId))
                    map[explicitId] = tool;

                if (string.IsNullOrEmpty(definitionId))
                    AddToPool(tool);
            }

            var assignedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < availabilities.Count; i++)
            {
                var entry = availabilities[i];
                var id = NormalizeSkillId(entry.skillId);
                if (string.IsNullOrEmpty(id))
                    continue;
                if (!assignedIds.Add(id))
                    continue;

                SkillDefinitionActionTool tool = null;
                if (map.TryGetValue(id, out var existing) && existing != null)
                    tool = existing;

                if (tool == null)
                {
                    if (pool.Count == 0)
                    {
                        Debug.LogWarning($"[ActionBinder] No SkillDefinitionActionTool available for {id}.", go);
                        continue;
                    }

                    int lastIndex = pool.Count - 1;
                    tool = pool[lastIndex];
                    pool.RemoveAt(lastIndex);
                    poolSet.Remove(tool);
                }
                else
                {
                    RemoveFromPool(tool);
                }

                if (skillIndex != null && skillIndex.TryGet(id, out var info) && info.definition != null)
                {
                    if (!ReferenceEquals(tool.Definition, info.definition))
                        tool.SetDefinition(info.definition);
                }
                else if (tool.Definition == null || !string.Equals(tool.Definition.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning($"[ActionBinder] Missing SkillDefinition for {id}.", tool);
                }

                map[id] = tool;
            }
        }

        static string NormalizeSkillId(string skillId)
            => string.IsNullOrWhiteSpace(skillId) ? null : skillId.Trim();

        void TrackUnit(Unit unit, UnitFaction faction, GameObject go, UnitRuntimeContext context, CooldownHubV2 hub, UnitGridAdapter adapter, Sprite avatar)
        {
            var record = new SpawnRecord
            {
                gameObject = go,
                context = context,
                cooldownHub = hub,
                gridAdapter = adapter
            };

            _spawned[unit] = record;

            TryRegisterUnitView(unit, go, avatar);
        }

        void TryRegisterUnitView(Unit unit, GameObject go, Sprite avatar)
        {
            if (unit == null || go == null)
                return;

            var binder = go.GetComponentInChildren<UnitViewBinding>(true);
            if (binder == null)
                binder = go.AddComponent<UnitViewBinding>();

            if (binder != null)
            {
                if (!binder.HasExplicitViewTransform)
                {
                    var candidate = go.transform.Find("UnitView");
                    if (candidate != null)
                        binder.SetViewTransform(candidate);
                }
                binder.Bind(unit);
                binder.SetAvatar(avatar);
                return;
            }
        }

        void EnsureDefaultValidatorInjected(HexOccupancyService occSvc, TurnManagerV2 tm)
        {
            if (tm == null)
                tm = turnManager;

            if (occSvc == null)
                occSvc = ResolveOccupancyService();

            var validator = _sharedValidator != null ? _sharedValidator : FindOne<DefaultTargetValidator>();
            if (validator == null)
                return;

            _sharedValidator = validator;

            HexEnvironmentSystem env = null;
            if (occSvc != null)
            {
                env = occSvc.GetComponent<HexEnvironmentSystem>();
                if (!env && occSvc.authoring != null)
                    env = occSvc.authoring.GetComponent<HexEnvironmentSystem>() ?? occSvc.authoring.GetComponentInParent<HexEnvironmentSystem>(true);
            }

            validator.InjectServices(tm, occSvc, env);
        }
        static T FindOne<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>(UnityEngine.FindObjectsInactive.Include);
#else
    return UnityEngine.Object.FindObjectOfType<T>(true);
#endif
        }
    }
}
