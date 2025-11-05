using System.Collections.Generic;
using UnityEngine;
using TGD.CombatV2;
using TGD.CoreV2;
using TGD.DataV2;
using TGD.HexBoard;

namespace TGD.LevelV2
{
    [DisallowMultipleComponent]
    public sealed class UnitFactory : MonoBehaviour
    {
        [Header("Deps (assign in Inspector)")]
        public TurnManagerV2 turnManager;
        public CombatActionManagerV2 cam;
        public HexOccupancyService board;
        public Transform unitRoot;

        [Header("Prefab Source")]
        [Tooltip("Optional default prefab to instantiate when spawning units.")]
        public GameObject defaultPrefab;

        [Header("Battle Control")]
        [Tooltip("Automatically call StartBattle after at least one unit is spawned.")]
        public bool autoStartBattle;

        readonly Dictionary<Unit, SpawnRecord> _spawned = new();
        readonly List<Unit> _friendlies = new();
        readonly List<Unit> _enemies = new();
        readonly HashSet<string> _usedIds = new();
        bool _battleStarted;

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

            var final = UnitComposeService.Compose(blueprint);
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

            go.name = $"{ResolveDisplayName(final, unitId)} ({final.faction})";
            PlaceTransform(go.transform, spawnHex);

            var adapter = RegisterOccupancy(unit, spawnHex, unit.Facing);
            RegisterTurnSystems(unit, context, cooldownHub, final.faction);
            WireActionComponents(go, context, cooldownHub);

            TrackUnit(unit, final.faction, go, context, cooldownHub, adapter);

            Debug.Log($"[Factory] Spawn {ResolveDisplayName(final, unitId)} ({final.faction}) at {spawnHex}", this);

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
                if (string.IsNullOrWhiteSpace(ability.actionId))
                    continue;
                hub.secStore.StartSeconds(ability.actionId.Trim(), Mathf.Max(0, ability.initialCooldownSeconds));
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
            else if (board != null && board.authoring != null && board.authoring.Layout != null)
            {
                position = board.authoring.Layout.World(spawnHex, board.authoring.y);
            }
            else
            {
                position = new Vector3(spawnHex.q, 0f, spawnHex.r);
            }

            target.position = position;
        }

        UnitGridAdapter RegisterOccupancy(Unit unit, Hex spawnHex, Facing4 facing)
        {
            var occ = ResolveOccupancy();
            if (occ == null)
                return null;

            var adapter = new UnitGridAdapter(unit, null);
            if (!occ.TryPlace(adapter, spawnHex, facing))
                Debug.LogWarning($"[Factory] Occupancy blocked for {unit.Id} at {spawnHex}", this);
            return adapter;
        }

        HexOccupancy ResolveOccupancy()
        {
            HexOccupancyService service = board;
            if (service == null && turnManager != null)
                service = turnManager.occupancyService;
            return service != null ? service.Get() : null;
        }

        void RegisterTurnSystems(Unit unit, UnitRuntimeContext context, CooldownHubV2 hub, UnitFaction faction)
        {
            if (turnManager != null && unit != null && context != null)
            {
                turnManager.Bind(unit, context);
            }

            var list = faction == UnitFaction.Friendly ? _friendlies : _enemies;
            if (!list.Contains(unit))
                list.Add(unit);
        }

        void WireActionComponents(GameObject go, UnitRuntimeContext context, CooldownHubV2 hub)
        {
            if (go == null)
                return;

            var mover = go.GetComponent<HexClickMover>();
            if (mover != null)
            {
                mover.ctx = context;
                mover.AttachTurnManager(turnManager);
            }

            var moveCost = go.GetComponent<MoveCostServiceV2Adapter>();
            if (moveCost != null)
            {
                if (moveCost.stats == null)
                    moveCost.stats = context != null ? context.stats : null;
                if (moveCost.cooldownHub == null)
                    moveCost.cooldownHub = hub;
                moveCost.ctx = context;
                moveCost.turnManager = turnManager;
            }

            var attack = go.GetComponent<AttackControllerV2>();
            if (attack != null)
            {
                attack.ctx = context;
                attack.turnManager = turnManager;
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
        }

        void TrackUnit(Unit unit, UnitFaction faction, GameObject go, UnitRuntimeContext context, CooldownHubV2 hub, UnitGridAdapter adapter)
        {
            var record = new SpawnRecord
            {
                gameObject = go,
                context = context,
                cooldownHub = hub,
                gridAdapter = adapter
            };

            _spawned[unit] = record;

            TryRegisterUnitView(unit, go);
        }

        void TryRegisterUnitView(Unit unit, GameObject go)
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
                return;
            }
        }
    }
}
