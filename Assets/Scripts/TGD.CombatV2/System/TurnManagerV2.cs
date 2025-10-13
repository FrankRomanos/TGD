using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TGD.CoreV2;
using TGD.HexBoard;
using UnityEngine;

namespace TGD.CombatV2
{
    public sealed class TurnManagerV2 : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("相位开始时的最小停顿（秒）")]
        public float phaseStartDelaySeconds = 1f;

        public event Action PlayerPhaseStarted;
        public event Action EnemyPhaseStarted;
        public event Action PlayerSideEnded;
        public event Action EnemySideEnded;
        public event Action<bool> PhaseBegan;
        public event Action<bool> SideEnded;
        public event Action<Unit> TurnStarted;
        public event Action<Unit> TurnEnded;
        public event Func<FullRoundQueuedAction, IEnumerator> FullRoundExecuteRequested;
        readonly List<Unit> _playerUnits = new();
        readonly List<Unit> _enemyUnits = new();

        readonly Dictionary<Unit, UnitRuntimeContext> _contextByUnit = new();
        readonly Dictionary<Unit, TurnRuntimeV2> _runtimeByUnit = new();
        readonly Dictionary<Unit, ITurnBudget> _budgetHandles = new();
        readonly Dictionary<Unit, IResourcePool> _resourceHandles = new();
        readonly Dictionary<Unit, ICooldownSink> _cooldownHandles = new();
        readonly Dictionary<Unit, MoveRateStatusRuntime> _moveRateStatuses = new();
        readonly Dictionary<UnitRuntimeContext, Unit> _unitByContext = new();
        readonly HashSet<UnitRuntimeContext> _allContexts = new();
        readonly HashSet<MoveRateStatusRuntime> _allMoveRateStatuses = new();
        readonly HashSet<CooldownStoreSecV2> _allCooldownStores = new();
        readonly List<FullRoundQueuedAction> _fullRoundQueue = new();

        public readonly struct FullRoundQueuedAction
        {
            public readonly Unit unit;
            public readonly IActionToolV2 tool;
            public readonly Hex hex;
            public readonly ActionCostPlan plan;
            public readonly bool executeAtNextOwnPhaseStart;

            public FullRoundQueuedAction(Unit unit, IActionToolV2 tool, Hex hex, ActionCostPlan plan, bool executeAtNextOwnPhaseStart)
            {
                this.unit = unit;
                this.tool = tool;
                this.hex = hex;
                this.plan = plan;
                this.executeAtNextOwnPhaseStart = executeAtNextOwnPhaseStart;
            }
        }

        [Header("Environment")]
        public HexEnvironmentSystem environment;

        [Header("Board Occupancy")]
        public HexOccupancyService occupancyService;

        Coroutine _loop;
        Unit _activeUnit;
        bool _waitingForEnd;
        int _phaseIndex;
        int _currentPhaseIndex;
        bool _currentPhaseIsPlayer;

        sealed class TurnBudgetHandle : ITurnBudget
        {
            readonly TurnManagerV2 _manager;
            readonly TurnRuntimeV2 _runtime;

            public TurnBudgetHandle(TurnManagerV2 manager, TurnRuntimeV2 runtime)
            {
                _manager = manager;
                _runtime = runtime;
            }

            public int Remaining => _runtime.RemainingTime;

            public bool HasTime(int seconds)
            {
                if (seconds <= 0) return true;
                return _runtime.RemainingTime >= seconds;
            }

            public void SpendTime(int seconds)
            {
                if (seconds <= 0) return;
                _manager.ApplyTimeSpend(_runtime, seconds);
            }

            public void RefundTime(int seconds)
            {
                if (seconds <= 0) return;
                _manager.ApplyTimeRefund(_runtime, seconds);
            }
        }

        sealed class ResourcePoolHandle : IResourcePool
        {
            readonly TurnManagerV2 _manager;
            readonly TurnRuntimeV2 _runtime;

            public ResourcePoolHandle(TurnManagerV2 manager, TurnRuntimeV2 runtime)
            {
                _manager = manager;
                _runtime = runtime;
            }

            public bool Has(string id, int value)
            {
                if (value <= 0) return true;
                return _manager.GetResourceCurrent(_runtime, id) >= value;
            }

            public void Spend(string id, int value, string reason = "")
            {
                if (value <= 0) return;
                _manager.ModifyResource(_runtime, id, -Mathf.Abs(value), reason, false);
            }

            public void Refund(string id, int value, string reason = "")
            {
                if (value <= 0) return;
                _manager.ModifyResource(_runtime, id, Mathf.Abs(value), reason, true);
            }

            public int Get(string id) => _manager.GetResourceCurrent(_runtime, id);

            public int GetMax(string id) => _manager.GetResourceMax(_runtime, id);
        }

        sealed class CooldownSinkHandle : ICooldownSink
        {
            readonly TurnManagerV2 _manager;
            readonly TurnRuntimeV2 _runtime;

            public CooldownSinkHandle(TurnManagerV2 manager, TurnRuntimeV2 runtime)
            {
                _manager = manager;
                _runtime = runtime;
            }

            CooldownStoreSecV2 Store => _manager.GetSecStore(_runtime);

            public bool Ready(string skillId)
            {
                var store = Store;
                if (store == null || string.IsNullOrEmpty(skillId)) return true;
                return store.Ready(skillId);
            }

            public void StartSeconds(string skillId, int seconds)
            {
                if (string.IsNullOrEmpty(skillId)) return;
                var store = Store;
                if (store == null) return;
                store.StartSeconds(skillId, seconds);
                int turns = store.TurnsLeft(skillId);
                _manager.LogCooldownStart(_runtime, skillId, seconds, turns);
            }

            public void AddSeconds(string skillId, int deltaSeconds)
            {
                if (string.IsNullOrEmpty(skillId) || deltaSeconds == 0) return;
                var store = Store;
                if (store == null) return;
                int left = store.AddSeconds(skillId, deltaSeconds);
                int turns = store.TurnsLeft(skillId);
                _manager.LogCooldownAdd(_runtime, skillId, deltaSeconds, left, turns);
            }

            public int SecondsLeft(string skillId)
            {
                var store = Store;
                return store != null ? store.SecondsLeft(skillId) : 0;
            }

            public int TurnsLeft(string skillId)
            {
                var store = Store;
                return store != null ? store.TurnsLeft(skillId) : 0;
            }
        }

        public void Bind(Unit unit, UnitRuntimeContext context)
        {
            if (unit == null || context == null) return;
            _contextByUnit[unit] = context;
            _unitByContext[context] = unit;
            RegisterContext(context);
            if (context.cooldownHub != null && context.cooldownHub.secStore != null)
                RegisterCooldownStore(context.cooldownHub.secStore);
            if (_runtimeByUnit.TryGetValue(unit, out var runtime))
                runtime.Bind(context);
        }
        public UnitRuntimeContext GetContext(Unit unit)
        {
            if (unit == null) return null;
            if (_contextByUnit.TryGetValue(unit, out var context))
                return context;
            if (_runtimeByUnit.TryGetValue(unit, out var runtime))
                return runtime.Context;
            return null;
        }

        public void StartBattle(List<Unit> players, Unit boss)
        {
            _playerUnits.Clear();
            if (players != null)
                _playerUnits.AddRange(players.Where(u => u != null));

            _enemyUnits.Clear();
            if (boss != null)
                _enemyUnits.Add(boss);

            foreach (var p in _playerUnits)
                EnsureRuntime(p, true);
            foreach (var e in _enemyUnits)
                EnsureRuntime(e, false);

            if (_loop != null)
                StopCoroutine(_loop);
            _phaseIndex = 0;
            _loop = StartCoroutine(BattleLoop());
        }

        public ITurnBudget GetBudget(Unit unit)
        {
            var runtime = EnsureRuntime(unit, null);
            if (runtime == null) return null;
            if (!_budgetHandles.TryGetValue(unit, out var handle))
            {
                handle = new TurnBudgetHandle(this, runtime);
                _budgetHandles[unit] = handle;
            }
            return handle;
        }

        public IResourcePool GetResources(Unit unit)
        {
            var runtime = EnsureRuntime(unit, null);
            if (runtime == null) return null;
            if (!_resourceHandles.TryGetValue(unit, out var handle))
            {
                handle = new ResourcePoolHandle(this, runtime);
                _resourceHandles[unit] = handle;
            }
            return handle;
        }

        public ICooldownSink GetCooldowns(Unit unit)
        {
            var runtime = EnsureRuntime(unit, null);
            if (runtime == null) return null;
            if (!_cooldownHandles.TryGetValue(unit, out var handle))
            {
                handle = new CooldownSinkHandle(this, runtime);
                _cooldownHandles[unit] = handle;
            }
            return handle;
        }
        public void EnqueueFullRound(FullRoundQueuedAction entry)
        {
            if (entry.unit == null || entry.tool == null)
                return;
            _fullRoundQueue.Add(entry);
        }

        public int GetTurnTime(Unit unit)
        {
            var runtime = EnsureRuntime(unit, null);
            return runtime != null ? runtime.TurnTime : 0;
        }

        public void EndTurn(Unit unit)
        {
            if (unit == null) return;
            var runtime = EnsureRuntime(unit, null);
            if (runtime == null) return;

            string unitLabel = FormatUnitLabel(runtime.Unit);
            Debug.Log($"[Turn] End T{_currentPhaseIndex}({unitLabel})", this);

            if (_activeUnit == unit)
            {
                _activeUnit = null;
                _waitingForEnd = false;
            }

            runtime.FinishTurn();
            TurnEnded?.Invoke(runtime.Unit);
        }

        IEnumerator BattleLoop()
        {
            while (true)
            {
                yield return RunPhase(_playerUnits, true);
                yield return RunPhase(_enemyUnits, false);
            }
        }

        IEnumerator RunPhase(List<Unit> units, bool isPlayer)
        {
            _phaseIndex += 1;
            _currentPhaseIndex = _phaseIndex;
            _currentPhaseIsPlayer = isPlayer;

            string phaseLabel = FormatPhaseLabel(isPlayer);
            Debug.Log($"[Phase] Begin T{_currentPhaseIndex}({phaseLabel})", this);
            if (isPlayer)
                OnPlayerPhaseBegin();
            else
                OnEnemyPhaseBegin();
            yield return ExecuteFullRoundQueue(isPlayer);

            float delay = Mathf.Max(1f, Mathf.Max(0f, phaseStartDelaySeconds));
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            Debug.Log($"[Phase] Idle T{_currentPhaseIndex}({phaseLabel}) ≥1s", this);

            foreach (var unit in units)
            {
                if (unit == null) continue;
                var runtime = EnsureRuntime(unit, isPlayer);
                if (runtime == null) continue;

                BeginTurn(runtime);
                yield return new WaitUntil(() => !_waitingForEnd || _activeUnit != unit);
            }
            if (isPlayer)
                OnPlayerSideEnd();
            else
                OnEnemySideEnd();
        }

        void BeginTurn(TurnRuntimeV2 runtime)
        {
            int turnTime = runtime.TurnTime;
            int prepaid = Mathf.Clamp(runtime.PrepaidTime, 0, turnTime);
            runtime.BeginTurn();
            _activeUnit = runtime.Unit;
            _waitingForEnd = true;
            string unitLabel = FormatUnitLabel(runtime.Unit);
            Debug.Log($"[Turn] Begin T{_currentPhaseIndex}({unitLabel}) TT={turnTime} Prepaid={prepaid} Remain={runtime.RemainingTime}", this);
            TurnStarted?.Invoke(runtime.Unit);
        }
        IEnumerator ExecuteFullRoundQueue(bool isPlayerPhase)
        {
            if (_fullRoundQueue.Count == 0)
                yield break;

            List<FullRoundQueuedAction> pending = null;
            for (int i = _fullRoundQueue.Count - 1; i >= 0; i--)
            {
                var entry = _fullRoundQueue[i];
                if (!entry.executeAtNextOwnPhaseStart)
                    continue;

                var runtime = EnsureRuntime(entry.unit, null);
                bool belongsToPhase = runtime != null ? runtime.IsPlayer == isPlayerPhase : isPlayerPhase ? IsPlayerUnit(entry.unit) : IsEnemyUnit(entry.unit);
                if (!belongsToPhase)
                    continue;

                pending ??= new List<FullRoundQueuedAction>();
                pending.Add(entry);
                _fullRoundQueue.RemoveAt(i);
            }

            if (pending == null || pending.Count == 0)
                yield break;

            pending.Reverse();

            foreach (var entry in pending)
            {
                var handler = FullRoundExecuteRequested;
                if (handler != null)
                {
                    var routine = handler(entry);
                    if (routine != null)
                        yield return routine;
                }
            }
        }
        void ApplyTimeSpend(TurnRuntimeV2 runtime, int seconds)
        {
            runtime.SpendTime(seconds);
            Debug.Log($"[Time] Spend {FormatUnitLabel(runtime.Unit)} {seconds}s -> Remain={runtime.RemainingTime}", this);
        }

        void ApplyTimeRefund(TurnRuntimeV2 runtime, int seconds)
        {
            runtime.RefundTime(seconds);
            Debug.Log($"[Time] Refund {FormatUnitLabel(runtime.Unit)} {seconds}s -> Remain={runtime.RemainingTime}", this);
        }

        void ModifyResource(TurnRuntimeV2 runtime, string id, int delta, string reason, bool isRefund)
        {
            if (runtime == null || string.IsNullOrEmpty(id) || delta == 0) return;

            int before = GetResourceCurrent(runtime, id);
            int after = before + delta;
            int maxBefore = GetResourceMax(runtime, id);
            int maxAfter = maxBefore;

            if (IsEnergy(id) && runtime.Context != null && runtime.Context.stats != null)
            {
                var stats = runtime.Context.stats;
                maxAfter = Mathf.Max(0, stats.MaxEnergy);
                stats.Energy = Mathf.Clamp(after, 0, maxAfter);
                after = stats.Energy;
            }
            else
            {
                int clampMax = maxBefore > 0 ? maxBefore : int.MaxValue;
                after = Mathf.Clamp(after, 0, clampMax);
                runtime.CustomResources[id] = after;
                maxAfter = clampMax == int.MaxValue
                    ? Mathf.Max(after, runtime.CustomResourceMax.TryGetValue(id, out var exist) ? exist : after)
                    : clampMax;
                runtime.CustomResourceMax[id] = maxAfter;
            }

            string suffix = string.IsNullOrEmpty(reason) ? string.Empty : $" ({reason})";
            string unitLabel = FormatUnitLabel(runtime.Unit);
            if (isRefund)
                Debug.Log($"[Res] Refund {unitLabel}:{id} +{Mathf.Abs(delta)} -> {after}/{Mathf.Max(0, maxAfter)}{suffix}", this);
            else
                Debug.Log($"[Res] Spend {unitLabel}:{id} -{Mathf.Abs(delta)} -> {after}/{Mathf.Max(0, maxAfter)}{suffix}", this);
        }

    
        (int gain, int current, int max) HandleEnergyRegen(TurnRuntimeV2 runtime)
        {
            var ctx = runtime.Context;
            var stats = ctx != null ? ctx.stats : null;
            if (stats == null)
            {
                return (0, 0, 0);
            }

            int turnTime = runtime.TurnTime;
            float chunks = Mathf.Max(0, turnTime) / 2f;
            int gain = Mathf.FloorToInt(chunks * Mathf.Max(0, stats.EnergyRegenPer2s));
            int max = Mathf.Max(0, stats.MaxEnergy);
            int before = stats.Energy;
            stats.Energy = Mathf.Clamp(before + gain, 0, max);
            return (gain, stats.Energy, max);
        }
 
        int GetResourceCurrent(TurnRuntimeV2 runtime, string id)
        {
            if (runtime == null || string.IsNullOrEmpty(id)) return 0;
            if (IsEnergy(id))
            {
                if (runtime.Context != null && runtime.Context.stats != null)
                    return runtime.Context.stats.Energy;
            }
            return runtime.CustomResources.TryGetValue(id, out var value) ? value : 0;
        }

        int GetResourceMax(TurnRuntimeV2 runtime, string id)
        {
            if (runtime == null || string.IsNullOrEmpty(id)) return 0;
            if (IsEnergy(id))
            {
                if (runtime.Context != null && runtime.Context.stats != null)
                    return runtime.Context.stats.MaxEnergy;
            }
            return runtime.CustomResourceMax.TryGetValue(id, out var value) ? value : 0;
        }

        CooldownStoreSecV2 GetSecStore(TurnRuntimeV2 runtime)
        {
            if (runtime?.Context == null) return null;
            var hub = runtime.Context.cooldownHub;
            return hub != null ? hub.secStore : null;
        }

        void LogCooldownStart(TurnRuntimeV2 runtime, string skillId, int seconds, int turns)
        {
            Debug.Log($"[CD] StartSeconds {FormatUnitLabel(runtime.Unit)}:{skillId} = {seconds}s (turns={turns})", this);
        }

        void LogCooldownAdd(TurnRuntimeV2 runtime, string skillId, int delta, int left, int turns)
        {
            Debug.Log($"[CD] AddSeconds {FormatUnitLabel(runtime.Unit)}:{skillId} += {delta}s -> {left}s (turns={turns})", this);
        }

        TurnRuntimeV2 EnsureRuntime(Unit unit, bool? isPlayerHint)
        {
            if (unit == null) return null;
            if (!_runtimeByUnit.TryGetValue(unit, out var runtime))
            {
                bool isPlayer = isPlayerHint ?? _playerUnits.Contains(unit);
                _contextByUnit.TryGetValue(unit, out var ctx);
                runtime = new TurnRuntimeV2(unit, ctx, isPlayer);
                _runtimeByUnit[unit] = runtime;
                if (ctx != null)
                {
                    _unitByContext[ctx] = unit;
                    RegisterContext(ctx);
                    if (ctx.cooldownHub != null && ctx.cooldownHub.secStore != null)
                        RegisterCooldownStore(ctx.cooldownHub.secStore);
                }
            }
            else if (isPlayerHint.HasValue && runtime.IsPlayer != isPlayerHint.Value)
            {
                // nothing for now, runtime tracks initial flag
            }

            if (_contextByUnit.TryGetValue(unit, out var context) && runtime.Context != context)
            {
                runtime.Bind(context);
                if (context != null)
                {
                    _unitByContext[context] = unit;
                    RegisterContext(context);
                    if (context.cooldownHub != null && context.cooldownHub.secStore != null)
                        RegisterCooldownStore(context.cooldownHub.secStore);
                }
            }

            return runtime;
        }
       
        string FormatContextLabel(UnitRuntimeContext context)
        {
            if (context == null)
                return "?";

            if (_unitByContext.TryGetValue(context, out var unit) && unit != null)
                return FormatUnitLabel(unit);

            foreach (var pair in _contextByUnit)
            {
                if (pair.Value == context)
                {
                    if (pair.Key != null)
                    {
                        _unitByContext[context] = pair.Key;
                        return FormatUnitLabel(pair.Key);
                    }
                }
            }
            return string.IsNullOrEmpty(context.name) ? "?" : context.name;
        }

        internal void RegisterMoveRateStatus(MoveRateStatusRuntime runtime)
        {
            if (runtime == null)
                return;
            var unit = runtime.UnitRef;
            if (unit == null)
                return;
            _moveRateStatuses[unit] = runtime;
            _allMoveRateStatuses.Add(runtime);

            if (_contextByUnit.TryGetValue(unit, out var context) && context != null)
            {
                _unitByContext[context] = unit;
                RegisterContext(context);
            }
        }

        internal void UnregisterMoveRateStatus(MoveRateStatusRuntime runtime)
        {
            if (runtime == null)
                return;
            var unit = runtime.UnitRef;
            if (unit == null)
                return;
            if (_moveRateStatuses.TryGetValue(unit, out var existing) && existing == runtime)
                _moveRateStatuses.Remove(unit);
            _allMoveRateStatuses.Remove(runtime);
        }

        internal void RegisterContext(UnitRuntimeContext context)
        {
            if (context == null)
                return;
            _allContexts.Add(context);
            if (context.cooldownHub != null && context.cooldownHub.secStore != null)
                RegisterCooldownStore(context.cooldownHub.secStore);
        }

        internal void UnregisterContext(UnitRuntimeContext context)
        {
            if (context == null)
                return;
            _allContexts.Remove(context);
            _unitByContext.Remove(context);
        }

        internal void RegisterCooldownStore(CooldownStoreSecV2 store)
        {
            if (store == null)
                return;
            _allCooldownStores.Add(store);
        }

        internal void UnregisterCooldownStore(CooldownStoreSecV2 store)
        {
            if (store == null)
                return;
            _allCooldownStores.Remove(store);
        }

        static bool IsEnergy(string id) => string.Equals(id, "Energy", StringComparison.OrdinalIgnoreCase);

        internal static string FormatUnitLabel(Unit unit)
        {
            if (unit == null)
                return "?";
            if (!string.IsNullOrEmpty(unit.Id))
                return unit.Id;
            return "?";
        }

        static string FormatPhaseLabel(bool isPlayer) => isPlayer ? "Player" : "Enemy";
        void OnPlayerPhaseBegin()
        {
            PlayerPhaseStarted?.Invoke();
            PhaseBegan?.Invoke(true);
            RefreshSideUnits(_playerUnits, true);
        }

        void OnEnemyPhaseBegin()
        {
            EnemyPhaseStarted?.Invoke();
            PhaseBegan?.Invoke(false);
            RefreshSideUnits(_enemyUnits, false);
        }

        void OnPlayerSideEnd()
        {
            ApplySideEndTicks(true);
            ClearTempAttackLayer("SideEnd");
            PlayerSideEnded?.Invoke();
            SideEnded?.Invoke(true);
        }

        void OnEnemySideEnd()
        {
            ApplySideEndTicks(false);
            ClearTempAttackLayer("SideEnd");
            EnemySideEnded?.Invoke();
            SideEnded?.Invoke(false);
        }

        void ClearTempAttackLayer(string reason)
        {
            var occ = occupancyService != null ? occupancyService.Get() : null;
            if (occ == null) return;
            int count = occ.ClearLayer(OccLayer.TempAttack);
            Debug.Log($"[Occ] TempClear {reason} count={count}", this);
        }

        readonly struct TerrainStickySample
        {
            public readonly bool hasSticky;
            public readonly float multiplier;
            public readonly int turns;
            public readonly string tag;

            public TerrainStickySample(bool hasSticky, float multiplier, int turns, string tag)
            {
                this.hasSticky = hasSticky;
                this.multiplier = multiplier;
                this.turns = turns;
                this.tag = tag;
            }

            public static TerrainStickySample None => new(false, 1f, 0, null);
        }

        void RefreshSideUnits(List<Unit> units, bool isPlayer)
        {
            if (units == null) return;
            foreach (var unit in units)
            {
                if (unit == null) continue;
                var runtime = EnsureRuntime(unit, isPlayer);
                if (runtime == null) continue;

                var context = runtime.Context;
                if (context == null) continue;

                float before = context.CurrentMoveRate;
                MoveRateStatusRuntime status = null;
                if (_moveRateStatuses.TryGetValue(unit, out var found) && found != null)
                    status = found;

                if (status != null)
                {
                    var terrainSample = SampleTerrain(unit.Position);
                    if (terrainSample.hasSticky)
                        status.RefreshFromTerrain(unit, unit.Position, terrainSample.multiplier, terrainSample.turns, terrainSample.tag);
                    status.RefreshProduct();
                }

                float product = status?.GetProduct() ?? 1f;
                float recomputed = StatsMathV2.MR_MultiThenFlat(context.BaseMoveRate, new[] { product }, context.MoveRateFlatAdd);
                context.CurrentMoveRate = recomputed;
                string unitLabel = FormatUnitLabel(unit);
                string tagsCsv = status?.ActiveTagsCsv ?? "none";
                Debug.Log($"[Buff]  Refresh U={unitLabel} mr:{before:F2} -> {recomputed:F2} (recomputed tags:{tagsCsv})", this);
            }
        }

        TerrainStickySample SampleTerrain(Hex hex)
        {
            if (environment != null && environment.TryGetSticky(hex, out var mult, out var turns, out var tag))
            {
                if (turns > 0 && !Mathf.Approximately(mult, 1f))
                {
                    string resolvedTag = string.IsNullOrEmpty(tag) ? $"Patch@{hex.q},{hex.r}" : tag;
                    float clampedMult = Mathf.Clamp(mult, 0.01f, 100f);
                    int normalizedTurns = turns < 0 ? -1 : turns;
                    return new TerrainStickySample(true, clampedMult, normalizedTurns, resolvedTag);
                }
            }

            return TerrainStickySample.None;
        }

        void ApplySideEndTicks(bool isPlayerSide)
        {
            string phaseLabel = FormatPhaseLabel(isPlayerSide);
            var processed = new HashSet<Unit>();

            void Process(Unit unit, bool? isPlayerHint)
            {
                if (unit == null || !processed.Add(unit))
                    return;

                var runtime = EnsureRuntime(unit, isPlayerHint);
                if (runtime != null)
                    TickCooldownsForUnit(runtime, phaseLabel);              
                TickBuffsForUnit(unit, phaseLabel);
                if (runtime != null)
                    ApplyEnergyRegen(runtime, phaseLabel);
            }

            void ProcessAll(IEnumerable<Unit> units, bool? isPlayerHint)
            {
                if (units == null)
                    return;
                foreach (var unit in units)
                    Process(unit, isPlayerHint);
            }
            ProcessAll(_playerUnits, true);
            ProcessAll(_enemyUnits, false);
            ProcessAll(_contextByUnit.Keys.ToList(), null);
            ProcessAll(_runtimeByUnit.Keys.ToList(), null);
            ProcessAll(_moveRateStatuses.Keys.ToList(), null);
        }

        void TickCooldownsForUnit(TurnRuntimeV2 runtime, string phaseLabel)
        {
            var store = GetSecStore(runtime);
            string unitLabel = FormatUnitLabel(runtime.Unit);
            if (store == null)
            {
                Debug.Log($"[CD]    Tick   T{_currentPhaseIndex}({phaseLabel}) U={unitLabel} -{StatsMathV2.BaseTurnSeconds}s (skills:none)", this);
                return;
            }

            var entries = store.Entries.ToList();
            List<string> details = new();
            foreach (var kv in entries)
            {
                var skillId = kv.Key;
                if (string.IsNullOrEmpty(skillId))
                    continue;
                int before = kv.Value;
                if (before <= 0)
                    continue;
                int after = store.AddSeconds(skillId, -StatsMathV2.BaseTurnSeconds);
                if (after < 0)
                {
                    store.StartSeconds(skillId, 0);
                    after = 0;
                }
                details.Add($"{skillId}:{before}->{after}");
            }

            string detailText = details.Count > 0 ? string.Join(";", details) : "none";
            Debug.Log($"[CD]    Tick   T{_currentPhaseIndex}({phaseLabel}) U={unitLabel} -{StatsMathV2.BaseTurnSeconds}s (skills:{detailText})", this);
        }

        void TickBuffsForUnit(Unit unit, string phaseLabel)
        {
            string unitLabel = FormatUnitLabel(unit);
            if (!_moveRateStatuses.TryGetValue(unit, out var status) || status == null)
            {
                Debug.Log($"[Buff]  Tick   T{_currentPhaseIndex}({phaseLabel}) U={unitLabel} -1 turn (tags:none)", this);
                return;
            }

            status.TickAll(-1);
            string tagsCsv = status.ActiveTagsCsv;
            Debug.Log($"[Buff]  Tick   T{_currentPhaseIndex}({phaseLabel}) U={unitLabel} -1 turn (tags:{tagsCsv})", this);
        }

        void ApplyEnergyRegen(TurnRuntimeV2 runtime, string phaseLabel)
        {
            string unitLabel = FormatUnitLabel(runtime.Unit);
            var regen = HandleEnergyRegen(runtime);
            if (regen.max <= 0)
            {
                Debug.Log($"[Res]   Regen  T{_currentPhaseIndex}({phaseLabel}) U={unitLabel} +{regen.gain} -> {regen.current}/{regen.max}", this);
                return;
            }

            Debug.Log($"[Res]   Regen  T{_currentPhaseIndex}({phaseLabel}) U={unitLabel} +{regen.gain} -> {regen.current}/{regen.max} (EndTurnRegen)", this);
        }

        public bool IsPlayerUnit(Unit unit) => unit != null && _playerUnits.Contains(unit);
        public bool IsEnemyUnit(Unit unit) => unit != null && _enemyUnits.Contains(unit);
    }
}
