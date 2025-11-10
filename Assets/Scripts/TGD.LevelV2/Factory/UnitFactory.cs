using System;
using System.Collections.Generic;
using System.Text;
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
        bool _loggedMissingSkillIndex;

        struct SharedRefs
        {
            public TurnManagerV2 turnManager;
            public CombatActionManagerV2 cam;
            public HexOccupancyService occupancy;
            public HexBoardAuthoringLite authoring;
            public HexBoardTiler tiler;
            public DefaultTargetValidator validator;
            public HexEnvironmentSystem environment;
            public Transform view;
            public SkillIndex skillIndex;
            public Camera pickCamera;
        }

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
            InjectOccService(context);
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

            var resolvedSkillIndex = ResolveSkillIndex();
            var shared = ResolveShared(go, resolvedSkillIndex);

            RegisterTurnSystems(unit, context, cooldownHub, final.faction, shared.turnManager);

            var adapter = EnsureGridAdapter(go, unit);
            WireMovementAndAttack(go, context, cooldownHub, unit, ref shared);
            WireOccupancy(go, context, unit, final.faction, ref adapter, spawnHex, unit.Facing, ref shared);
            WireHazardWatchers(go, context, ref shared);

            TrackUnit(unit, final.faction, go, context, cooldownHub, adapter, final.avatar);

            Debug.Log($"[Factory] Spawn {ResolveDisplayName(final, unitId)} ({final.faction}) at {spawnHex}", this);

            var availabilities = UnitActionBinder.Bind(go, context, final.abilities);
            ApplyAbilityLoadout(go, context, availabilities, shared.cam, shared.skillIndex);

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

        static void InjectOccService(UnitRuntimeContext ctx)
        {
            if (ctx == null)
                return;

            var occAdapter = UnityEngine.Object.FindFirstObjectByType<HexOccServiceAdapter>(FindObjectsInactive.Include);
            if (occAdapter == null)
            {
                Debug.LogError("[Occ] HexOccServiceAdapter not found.");
                return;
            }

            ctx.occService = occAdapter;
            OccDiagnostics.AssertSingleStore(ctx.occService, "Factory.Spawn");
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

        void RegisterTurnSystems(Unit unit, UnitRuntimeContext context, CooldownHubV2 hub, UnitFaction faction, TurnManagerV2 resolvedTurnManager)
        {
            var tm = resolvedTurnManager ?? turnManager;
            if (tm != null && unit != null)
            {
                tm.Bind(unit, context);
                if (turnManager == null)
                    turnManager = tm;
            }

            var list = faction == UnitFaction.Friendly ? _friendlies : _enemies;
            if (!list.Contains(unit))
                list.Add(unit);
        }

        SharedRefs ResolveShared(GameObject go, SkillIndex resolvedSkillIndex)
        {
            var shared = new SharedRefs
            {
                skillIndex = resolvedSkillIndex ?? ResolveSkillIndex(),
                turnManager = turnManager != null ? turnManager : FindOne<TurnManagerV2>(),
                cam = cam != null ? cam : FindOne<CombatActionManagerV2>()
            };

            if (shared.turnManager != null && turnManager == null)
                turnManager = shared.turnManager;
            if (shared.cam != null && cam == null)
                cam = shared.cam;

            if (shared.cam != null && shared.cam.pickCamera != null)
                shared.pickCamera = shared.cam.pickCamera;

            if (shared.pickCamera == null)
                shared.pickCamera = Camera.main;

            if (shared.cam != null && shared.cam.rulebook != null && shared.skillIndex != null)
            {
                if (shared.cam.rulebook.includeSkillIndexDerivedLinks)
                    shared.cam.rulebook.skillIndex = shared.skillIndex;
            }

            shared.occupancy = occupancyService != null ? occupancyService : (shared.turnManager != null ? shared.turnManager.occupancyService : null);
            if (shared.occupancy == null)
                shared.occupancy = ResolveOccupancyService();
            if (occupancyService == null && shared.occupancy != null)
                occupancyService = shared.occupancy;

            shared.authoring = shared.occupancy != null ? shared.occupancy.authoring : null;
            if (shared.authoring == null && shared.cam != null)
                shared.authoring = shared.cam.authoring;
            if (shared.authoring == null)
                shared.authoring = FindOne<HexBoardAuthoringLite>();

            shared.tiler = shared.cam != null ? shared.cam.tiler : null;
            if (shared.tiler == null && shared.authoring != null)
                shared.tiler = shared.authoring.GetComponentInChildren<HexBoardTiler>(true);
            if (shared.tiler == null)
                shared.tiler = FindOne<HexBoardTiler>();

            if (shared.occupancy != null)
            {
                shared.environment = shared.occupancy.GetComponent<HexEnvironmentSystem>();
                if (shared.environment == null && shared.occupancy.authoring != null)
                {
                    shared.environment = shared.occupancy.authoring.GetComponent<HexEnvironmentSystem>()
                                         ?? shared.occupancy.authoring.GetComponentInParent<HexEnvironmentSystem>(true);
                }
            }
            if (shared.environment == null && shared.authoring != null)
            {
                shared.environment = shared.authoring.GetComponent<HexEnvironmentSystem>()
                                     ?? shared.authoring.GetComponentInParent<HexEnvironmentSystem>(true);
            }
            if (shared.environment == null && shared.turnManager != null)
                shared.environment = shared.turnManager.environment;
            if (shared.environment == null)
                shared.environment = FindOne<HexEnvironmentSystem>();

            shared.validator = EnsureLocalTargetValidator(go, shared.turnManager ?? turnManager, shared.occupancy, shared.environment);

            shared.view = go != null
                ? go.GetComponentInChildren<Animator>(true)?.transform
                  ?? go.GetComponentInChildren<SkinnedMeshRenderer>(true)?.transform
                  ?? go.transform
                : null;

            return shared;
        }

        void WireMovementAndAttack(
            GameObject go,
            UnitRuntimeContext context,
            CooldownHubV2 hub,
            Unit unit,
            ref SharedRefs shared)
        {
            if (go == null)
                return;

            var resolvedTurnManager = shared.turnManager ?? turnManager;
            var resolvedCam = shared.cam ?? cam;
            if (resolvedCam != null && cam == null)
                cam = resolvedCam;

            var resolvedAuthoring = shared.authoring;
            var resolvedTiler = shared.tiler;
            var resolvedOccupancy = shared.occupancy;
            var resolvedEnv = shared.environment;
            var resolvedValidator = shared.validator;
            var view = shared.view ?? go.transform;
            var resolvedCamera = shared.pickCamera != null ? shared.pickCamera : Camera.main;
            shared.pickCamera = resolvedCamera;
            if (resolvedEnv == null)
            {
                resolvedEnv = FindOne<HexEnvironmentSystem>();
                if (resolvedEnv != null)
                    shared.environment = resolvedEnv;
            }

            resolvedValidator = EnsureLocalTargetValidator(go, resolvedTurnManager, resolvedOccupancy, resolvedEnv);
            shared.validator = resolvedValidator;

            var statuses = go.GetComponentsInChildren<MoveRateStatusRuntime>(true);
            MoveRateStatusRuntime primaryStatus = statuses != null && statuses.Length > 0 ? statuses[0] : null;

            MoveRateStatusRuntime ResolveStatusFor(Component owner)
            {
                if (owner == null)
                    return primaryStatus;

                var fromSelf = owner.GetComponent<MoveRateStatusRuntime>();
                if (fromSelf != null)
                    return fromSelf;

                var fromParent = owner.GetComponentInParent<MoveRateStatusRuntime>(true);
                if (fromParent != null)
                    return fromParent;

                var fromChildren = owner.GetComponentInChildren<MoveRateStatusRuntime>(true);
                if (fromChildren != null)
                    return fromChildren;

                return primaryStatus;
            }

            var movers = go.GetComponentsInChildren<HexClickMover>(true);
            foreach (var mover in movers)
            {
                if (mover == null)
                    continue;

                mover.BindContext(context, resolvedTurnManager);
                mover.authoring = resolvedAuthoring;
                mover.tiler = resolvedTiler;
                mover.targetValidator = resolvedValidator;
                mover.occupancyService = resolvedOccupancy;
                mover.env = resolvedEnv;
                mover.status = ResolveStatusFor(mover);
                mover.stickySource = resolvedEnv;
                mover.viewOverride = view;
                mover.pickCamera = resolvedCamera;
                if (resolvedCam != null)
                {
                    mover.pickMask = resolvedCam.pickMask;
                    mover.pickPlaneY = resolvedCam.pickPlaneY;
                    mover.rayMaxDistance = resolvedCam.rayMaxDistance;
                }
                mover.bridgeOverride = null;
                mover.RefreshFactoryInjection();
            }

            var attacks = go.GetComponentsInChildren<AttackControllerV2>(true);
            foreach (var attack in attacks)
            {
                if (attack == null)
                    continue;

                attack.BindContext(context, resolvedTurnManager);
                attack.turnManager = resolvedTurnManager;
                attack.authoring = resolvedAuthoring;
                attack.tiler = resolvedTiler;
                attack.targetValidator = resolvedValidator;
                attack.occupancyService = resolvedOccupancy;
                attack.env = resolvedEnv;
                attack.status = ResolveStatusFor(attack);
                attack.stickySource = resolvedEnv;
                attack.viewOverride = view;
                attack.bridgeOverride = null;
                attack.RefreshFactoryInjection();
            }

            var hudListeners = go.GetComponentsInChildren<ActionHudMessageListenerTMP>(true);
            foreach (var hud in hudListeners)
            {
                if (hud == null)
                    continue;

                hud.BindContext(context, resolvedTurnManager);
            }

            var attackAnimDrivers = go.GetComponentsInChildren<AttackAnimDriver>(true);
            foreach (var animDriver in attackAnimDrivers)
            {
                if (animDriver == null)
                    continue;

                animDriver.ctx = context;
                if (unit != null)
                    animDriver.BindUnit(unit);
            }

            var attackMoveListeners = go.GetComponentsInChildren<AttackMoveAnimListener>(true);
            foreach (var listener in attackMoveListeners)
            {
                if (listener == null)
                    continue;

                listener.ctx = context;
            }

            var chainActions = go.GetComponentsInChildren<ChainActionBase>(true);
            foreach (var chain in chainActions)
            {
                if (chain == null)
                    continue;

                chain.BindContext(context, resolvedTurnManager);
                chain.targetValidator = resolvedValidator;
                chain.tiler = resolvedTiler;
            }

            var unitMoveListeners = go.GetComponentsInChildren<UnitMoveAnimListener>(true);
            foreach (var listener in unitMoveListeners)
            {
                if (listener == null)
                    continue;

                listener.ctx = context;
                listener.mover = listener.GetComponent<HexClickMover>() ?? listener.GetComponentInParent<HexClickMover>(true);
            }

            var autoDrivers = go.GetComponentsInChildren<TestEnemyAutoActionDriver>(true);
            foreach (var driver in autoDrivers)
            {
                if (driver == null)
                    continue;

                driver.actionManager = resolvedCam;
                driver.turnManager = resolvedTurnManager;
            }

            foreach (var status in statuses)
            {
                if (status == null)
                    continue;

                status.BindContext(context, resolvedTurnManager);
            }
        }

        void WireHazardWatchers(GameObject go, UnitRuntimeContext context, ref SharedRefs shared)
        {
            if (go == null)
                return;

            var resolvedEnv = shared.environment;
            if (resolvedEnv == null)
            {
                resolvedEnv = FindOne<HexEnvironmentSystem>();
                if (resolvedEnv != null)
                    shared.environment = resolvedEnv;
            }

            var watchers = go.GetComponentsInChildren<HexHazardWatcher>(true);
            if (watchers == null || watchers.Length == 0)
                return;

            for (int i = 0; i < watchers.Length; i++)
            {
                var watcher = watchers[i];
                if (watcher == null)
                    continue;

                watcher.RefreshFactoryInjection(context, resolvedEnv);
            }
        }

        void WireOccupancy(
            GameObject go,
            UnitRuntimeContext context,
            Unit unit,
            UnitFaction faction,
            ref UnitGridAdapter adapter,
            Hex spawn,
            Facing4 facing,
            ref SharedRefs shared)
        {
            if (go == null)
                return;

            var occSvc = shared.occupancy;
            if (occSvc == null)
                occSvc = occupancyService;
            if (occSvc == null && shared.turnManager != null)
                occSvc = shared.turnManager.occupancyService;
            if (occSvc == null)
                occSvc = ResolveOccupancyService();

            shared.occupancy = occSvc;
            if (occupancyService == null && occSvc != null)
                occupancyService = occSvc;

            if (shared.environment == null && occSvc != null)
            {
                shared.environment = occSvc.GetComponent<HexEnvironmentSystem>();
                if (shared.environment == null && occSvc.authoring != null)
                {
                    shared.environment = occSvc.authoring.GetComponent<HexEnvironmentSystem>()
                                        ?? occSvc.authoring.GetComponentInParent<HexEnvironmentSystem>(true);
                }
            }

            if (shared.authoring == null && occSvc != null)
                shared.authoring = occSvc.authoring;

            var resolvedEnv = shared.environment;

            var bound = context != null && context.boundUnit != null ? context.boundUnit : unit;
            if (adapter == null)
                adapter = EnsureGridAdapter(go, bound);
            else if (adapter.Unit == null && bound != null)
                adapter.Unit = bound;

            var bridge = go.GetComponent<PlayerOccupancyBridge>() ?? go.AddComponent<PlayerOccupancyBridge>();
            if (bridge != null)
            {
                bridge.occupancyService = occSvc;
                if (defaultFootprint != null)
                    bridge.overrideFootprint = defaultFootprint;
            }

            if (bridge != null && adapter != null)
                bridge.Bind(adapter);

            var face = bound != null ? bound.Facing : facing;
            if (bridge != null)
            {
                bool placed = bridge.PlaceImmediate(spawn, face);
                if (!placed)
                {
                    Debug.LogWarning($"[Factory] Failed to immediately place {bound?.Id ?? go.name} at {spawn}.", go);
                    bridge.EnsurePlacedNow();
                }
            }

            if (bridge != null)
            {
                foreach (var mover in go.GetComponentsInChildren<HexClickMover>(true))
                {
                    if (mover == null)
                        continue;

                    mover.bridgeOverride = bridge;
                    mover.occupancyService = occSvc;
                    if (resolvedEnv != null)
                    {
                        mover.env = resolvedEnv;
                        if (mover.status == null)
                        {
                            mover.status = mover.GetComponent<MoveRateStatusRuntime>()
                                          ?? mover.GetComponentInParent<MoveRateStatusRuntime>(true)
                                          ?? mover.GetComponentInChildren<MoveRateStatusRuntime>(true);
                        }
                        mover.stickySource = resolvedEnv;
                        mover.RefreshFactoryInjection();
                    }
                }

                foreach (var attack in go.GetComponentsInChildren<AttackControllerV2>(true))
                {
                    if (attack == null)
                        continue;

                    attack.bridgeOverride = bridge;
                    attack.occupancyService = occSvc;
                    if (resolvedEnv != null)
                    {
                        attack.env = resolvedEnv;
                        if (attack.status == null)
                        {
                            attack.status = attack.GetComponent<MoveRateStatusRuntime>()
                                            ?? attack.GetComponentInParent<MoveRateStatusRuntime>(true)
                                            ?? attack.GetComponentInChildren<MoveRateStatusRuntime>(true);
                        }
                        attack.stickySource = resolvedEnv;
                        attack.RefreshFactoryInjection();
                    }
                }
            }

            var tm = shared.turnManager ?? turnManager;
            if (tm != null && bound != null)
            {
                bool isFriendly = faction == UnitFaction.Friendly;
                tm.RegisterSpawn(bound, isFriendly);
                bool isEnemy = tm.IsEnemyUnit(bound);
                bool isPlayer = tm.IsPlayerUnit(bound);
                Debug.Log($"[Factory] TM roster {bound.Id}: player={isPlayer} enemy={isEnemy}", this);
            }

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

            HexOccupancyService resolved = null;
            if (turnManager != null && turnManager.occupancyService != null)
                resolved = turnManager.occupancyService;

            if (resolved == null)
                resolved = FindOne<HexOccupancyService>();

            if (resolved != null && occupancyService == null)
                occupancyService = resolved;

            return resolved;
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

        static readonly HashSet<string> BuiltinSkillIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "Move",
            "Attack"
        };

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

            AssignSkillDefinitions(context, availabilities, skillIndex);

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
            var boundUnit = context != null ? context.boundUnit : null;
            string ownerLabel = boundUnit != null ? TurnManagerV2.FormatUnitLabel(boundUnit) : "?";
            bool advancedLogs = cam != null && cam.AdvancedDebugLogsEnabled;

            foreach (var behaviour in behaviours)
            {
                if (behaviour is IActionToolV2 tool)
                {
                    var id = NormalizeSkillId(tool.Id);
                    bool enable = !string.IsNullOrEmpty(id) && unlocked.Contains(id);
                    bool shouldRegister = !string.IsNullOrEmpty(id) && granted.Contains(id);
                    string idLabel = string.IsNullOrEmpty(id) ? "?" : id;
                    string toolType = behaviour != null ? behaviour.GetType().Name : "?";
                    ActionKind kind;
                    try
                    {
                        kind = tool.Kind;
                    }
                    catch (MissingReferenceException)
                    {
                        kind = ActionKind.Standard;
                    }

                    bool isSkillDefinitionTool = behaviour is SkillDefinitionActionTool;
                    bool isBuiltin = isSkillDefinitionTool && !string.IsNullOrEmpty(id) && BuiltinSkillIds.Contains(id);

                    if (isBuiltin)
                    {
                        enable = false;
                        shouldRegister = false;
                    }

                    if (advancedLogs)
                        Debug.Log($"[Binder] owner={ownerLabel} tool={toolType} id={idLabel} kind={kind} enable={enable} register={shouldRegister}", behaviour);

                    behaviour.enabled = enable;

                    if (cam != null)
                    {
                        if (shouldRegister)
                        {
                            cam.RegisterTool(tool);
                            int instanceId = behaviour != null ? behaviour.GetInstanceID() : 0;
                            if (advancedLogs)
                                Debug.Log($"[Binder] +Reg owner={ownerLabel} id={idLabel} inst={instanceId}", behaviour);
                        }
                        else
                        {
                            cam.UnregisterTool(tool);
                            if (advancedLogs)
                                Debug.Log($"[Binder] -Reg owner={ownerLabel} id={idLabel}", behaviour);
                        }
                    }
                }
            }
        }

        static void AssignSkillDefinitions(
            UnitRuntimeContext context,
            IReadOnlyList<UnitActionBinder.ActionAvailability> availabilities,
            SkillIndex skillIndex)
        {
            if (context == null)
                return;

            var used = new HashSet<SkillDefinitionActionTool>();
            var uniqueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (availabilities != null)
            {
                for (int i = 0; i < availabilities.Count; i++)
                {
                    var entry = availabilities[i];
                    var id = NormalizeSkillId(entry.skillId);
                    if (string.IsNullOrEmpty(id))
                        continue;
                    if (BuiltinSkillIds.Contains(id))
                        continue;
                    if (!uniqueIds.Add(id))
                        continue;

                    string slotName = ResolveToolSlotName(id);
                    string trayPath = $"ActionTools/{slotName}";
                    var tool = UnitRuntimeBindingUtil.EnsureToolOnTray<SkillDefinitionActionTool>(context, trayPath);
                    if (tool == null)
                    {
                        Debug.LogWarning($"[ActionBinder] Failed to ensure SkillDefinitionActionTool for {id}.", context);
                        continue;
                    }

                    tool.gameObject.name = slotName;
                    tool.SetId(id);
                    if (tool.Ctx != context)
                        tool.Bind(context);
                    tool.enabled = true;

                    if (skillIndex != null && skillIndex.TryGet(id, out var info) && info.definition != null)
                    {
                        if (!ReferenceEquals(tool.Definition, info.definition))
                            tool.SetDefinition(info.definition);
                    }
                    else if (tool.Definition == null || !string.Equals(NormalizeSkillId(tool.Definition.Id), id, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.LogWarning($"[ActionBinder] Missing SkillDefinition for {id}.", tool);
                    }

                    used.Add(tool);
                }
            }

            DisableUnusedSkillDefinitionTools(context, used);
        }

        static void DisableUnusedSkillDefinitionTools(UnitRuntimeContext context, HashSet<SkillDefinitionActionTool> used)
        {
            if (context == null)
                return;

            var root = context.transform;
            if (root == null)
                return;

            var tray = root.Find("ActionTools");
            if (tray == null)
                return;

            var tools = tray.GetComponentsInChildren<SkillDefinitionActionTool>(true);
            if (tools == null)
                return;

            for (int i = 0; i < tools.Length; i++)
            {
                var tool = tools[i];
                if (tool == null)
                    continue;

                var id = NormalizeSkillId(tool.Id);
                if (!string.IsNullOrEmpty(id) && BuiltinSkillIds.Contains(id))
                {
                    tool.enabled = false;
                    continue;
                }

                if (used != null && used.Contains(tool))
                    continue;

                tool.enabled = false;
            }
        }

        static string ResolveToolSlotName(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "Skill";

            var builder = new StringBuilder(id.Length);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (char.IsLetterOrDigit(c))
                    builder.Append(c);
                else
                    builder.Append('_');
            }

            var sanitized = builder.ToString().Trim('_');
            if (string.IsNullOrEmpty(sanitized))
                sanitized = "Skill";

            return $"Skill_{sanitized}";
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

        static DefaultTargetValidator EnsureLocalTargetValidator(GameObject go, TurnManagerV2 tm, HexOccupancyService occupancy, HexEnvironmentSystem environment)
        {
            if (go == null)
                return null;

            var validator = go.GetComponent<DefaultTargetValidator>();
            if (validator == null)
                validator = go.AddComponent<DefaultTargetValidator>();

            validator.InjectServices(tm, occupancy, environment);
            return validator;
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
