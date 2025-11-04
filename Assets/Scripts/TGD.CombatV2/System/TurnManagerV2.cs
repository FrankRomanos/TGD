using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TGD.CoreV2;
using TGD.HexBoard;
using TGD.CoreV2.Rules;
using UnityEngine;

namespace TGD.CombatV2
{
    public sealed class TurnManagerV2 : MonoBehaviour
    {
        public readonly struct TurnRuntimeSnapshot
        {
            public readonly Unit unit;
            public readonly int remaining;
            public readonly int baseTime;
            public readonly int prepaid;
            public readonly int turnTime;

            public TurnRuntimeSnapshot(Unit unit, int remaining, int baseTime, int prepaid, int turnTime)
            {
                this.unit = unit;
                this.remaining = Mathf.Max(0, remaining);
                this.baseTime = Mathf.Max(0, baseTime);
                this.prepaid = Mathf.Max(0, prepaid);
                this.turnTime = Mathf.Max(0, turnTime);
            }
        }

        [Header("Timing")]
        [Tooltip("相位开始时的最小停顿（秒）")]
        public float phaseStartDelaySeconds = 1f;
        [Tooltip("自动结束回合前的最小等待（秒）")]
        public float autoTurnEndDelaySeconds = 1f;

        public event Action PlayerPhaseStarted;
        public event Action EnemyPhaseStarted;
        public event Action PlayerSideEnded;
        public event Action EnemySideEnded;
        public event Action<bool> PhaseBegan;
        public event Action<bool> SideEnded;
        public event Action<Unit> TurnStarted;
        public event Action<Unit> TurnEnded;
        public event Action<Unit> UnitRuntimeChanged;
        public event Action<Unit> UnitTurnTimeDepleted;
        public event Action<bool> TurnOrderChanged;

        readonly List<Unit> _playerUnits = new();
        readonly List<Unit> _enemyUnits = new();
        public Unit ActiveUnit => _activeUnit;


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
        readonly List<Func<bool, IEnumerator>> _phaseStartGates = new();
        readonly List<Func<Unit, IEnumerator>> _turnStartGates = new();
        readonly Dictionary<Unit, FullRoundState> _fullRoundStates = new();
        readonly Dictionary<Unit, int> _autoEndGuards = new();
        readonly HashSet<Unit> _turnTimeDepletedUnits = new();

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
        List<Unit> _activePhaseUnits;
        int _activePhaseUnitIndex;
        int _activePhaseOverrideIndex = -1;

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

            public void SpendTime(int seconds, bool silent = false)
            {
                if (seconds <= 0) return;
                _manager.ApplyTimeSpend(_runtime, seconds, silent);
            }

            public void RefundTime(int seconds, bool silent = false)
            {
                if (seconds <= 0) return;
                _manager.ApplyTimeRefund(_runtime, seconds, silent);
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

            public void Spend(string id, int value, string reason = "", bool silent = false)
            {
                if (value <= 0) return;
                _manager.ModifyResource(_runtime, id, -Mathf.Abs(value), reason, false, silent);
            }

            public void Refund(string id, int value, string reason = "", bool silent = false)
            {
                if (value <= 0) return;
                _manager.ModifyResource(_runtime, id, Mathf.Abs(value), reason, true, silent);
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
        sealed class FullRoundState
        {
            public int roundsRemaining;
            public int totalRounds;
            public IFullRoundActionTool tool;
            public string actionId;
            public FullRoundQueuedPlan plan;
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

        public void RegisterPhaseStartGate(Func<bool, IEnumerator> gate)
        {
            if (gate == null)
                return;
            if (!_phaseStartGates.Contains(gate))
                _phaseStartGates.Add(gate);
        }

        public bool HasReachedIdle(Unit unit)
        {
            if (unit == null)
                return false;

            return _runtimeByUnit.TryGetValue(unit, out var runtime) && runtime != null && runtime.HasReachedIdle;
        }

        public void PushAutoTurnEndGuard(Unit unit)
        {
            if (unit == null)
                return;

            if (_autoEndGuards.TryGetValue(unit, out var count))
                _autoEndGuards[unit] = count + 1;
            else
                _autoEndGuards[unit] = 1;
        }

        public void PopAutoTurnEndGuard(Unit unit)
        {
            if (unit == null)
                return;

            if (_autoEndGuards.TryGetValue(unit, out var count))
            {
                count = Mathf.Max(0, count - 1);
                if (count <= 0)
                    _autoEndGuards.Remove(unit);
                else
                    _autoEndGuards[unit] = count;
            }
        }

        int GetAutoTurnEndGuard(Unit unit)
        {
            if (unit == null)
                return 0;

            return _autoEndGuards.TryGetValue(unit, out var count) ? count : 0;
        }

        public void UnregisterPhaseStartGate(Func<bool, IEnumerator> gate)
        {
            if (gate == null)
                return;
            _phaseStartGates.Remove(gate);
        }

        public void RegisterTurnStartGate(Func<Unit, IEnumerator> gate)
        {
            if (gate == null)
                return;
            if (!_turnStartGates.Contains(gate))
                _turnStartGates.Add(gate);
        }

        public void UnregisterTurnStartGate(Func<Unit, IEnumerator> gate)
        {
            if (gate == null)
                return;
            _turnStartGates.Remove(gate);
        }

        public bool IsPlayerPhase => _currentPhaseIsPlayer;
        public int CurrentPhaseIndex => _currentPhaseIndex;
        public IReadOnlyList<Unit> GetSideUnits(bool isPlayerSide) => isPlayerSide ? _playerUnits : _enemyUnits;
        public int GetTurnOrderIndex(Unit unit, bool isPlayerSide)
        {
            if (unit == null)
                return int.MaxValue;

            var list = isPlayerSide ? _playerUnits : _enemyUnits;
            if (list == null)
                return int.MaxValue;

            int index = list.IndexOf(unit);
            return index >= 0 ? index : int.MaxValue;
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

        public bool TryGetRuntimeSnapshot(Unit unit, out TurnRuntimeSnapshot snapshot)
        {
            snapshot = default;
            var runtime = EnsureRuntime(unit, null);
            if (runtime == null)
                return false;

            snapshot = new TurnRuntimeSnapshot(
                runtime.Unit,
                runtime.RemainingTime,
                runtime.BaseTimeForNext,
                runtime.PrepaidTime,
                runtime.TurnTime);
            return true;
        }

        public void StartBattle(List<Unit> players, List<Unit> enemies)
        {
            _playerUnits.Clear();
            if (players != null)
                _playerUnits.AddRange(players.Where(u => u != null));

            _enemyUnits.Clear();
            if (enemies != null)
                _enemyUnits.AddRange(enemies.Where(u => u != null));

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
        public bool HasActiveFullRound(Unit unit)
        {
            if (unit == null)
                return false;
            return _fullRoundStates.TryGetValue(unit, out var state) && state != null;
        }

        public bool TryGetFullRoundInfo(Unit unit, out int roundsRemaining, out int totalRounds, out string actionId)
        {
            roundsRemaining = 0;
            totalRounds = 0;
            actionId = null;

            if (unit == null)
                return false;

            if (_fullRoundStates.TryGetValue(unit, out var state) && state != null)
            {
                roundsRemaining = Mathf.Max(0, state.roundsRemaining);
                totalRounds = Mathf.Max(0, state.totalRounds);
                actionId = state.actionId;
                return true;
            }

            return false;
        }

        public bool CanDeclareFullRound(Unit unit, out string reason)
        {
            reason = null;
            if (unit == null)
            {
                reason = "noUnit";
                return false;
            }

            var runtime = EnsureRuntime(unit, null);
            if (runtime == null)
            {
                reason = "noRuntime";
                return false;
            }

            if (HasActiveFullRound(unit))
            {
                reason = "fullRoundActive";
                return false;
            }

            if (!runtime.HasReachedIdle)
            {
                reason = "notIdle";
                return false;
            }

            int baseTime = Mathf.Max(0, runtime.BaseTimeForNext);
            int remaining = Mathf.Max(0, runtime.RemainingTime);

            if (baseTime <= 0 || remaining <= 0)
            {
                reason = "lackTime";
                return false;
            }

            if (remaining != baseTime)
            {
                reason = "notFullBudget";
                return false;
            }

            return true;
        }

        public void RegisterFullRound(Unit unit, int rounds, string actionId, IFullRoundActionTool tool, FullRoundQueuedPlan plan)
        {
            if (unit == null || rounds <= 0)
                return;

            var state = new FullRoundState
            {
                roundsRemaining = rounds,
                totalRounds = rounds,
                tool = tool,
                actionId = actionId,
                plan = plan
            };

            _fullRoundStates[unit] = state;
            Debug.Log($"[FullRound] Queue U={FormatUnitLabel(unit)} id={actionId} rounds={rounds}", this);
        }
        public void EndTurn(Unit unit)
        {
            if (unit == null) return;
            var runtime = EnsureRuntime(unit, null);
            if (runtime == null) return;

            EnsureIdleLogged(runtime, runtime.ActivePhaseIndex);

            string unitLabel = FormatUnitLabel(runtime.Unit);
            Debug.Log($"[Turn] End T{_currentPhaseIndex}({unitLabel})", this);

            if (_activeUnit == unit)
            {
                _activeUnit = null;
                _waitingForEnd = false;
            }

            if (_autoEndGuards.ContainsKey(unit))
                _autoEndGuards.Remove(unit);

            runtime.FinishTurn();
            TurnEnded?.Invoke(runtime.Unit);
            RaiseUnitRuntimeChanged(runtime.Unit);
        }

        IEnumerator BattleLoop()
        {
            while (true)
            {
                yield return RunPhase(_playerUnits, true);
                yield return RunPhase(_enemyUnits, false);
            }
        }


        IEnumerator RunPhaseStartGates(bool isPlayer)
        {
            if (_phaseStartGates.Count == 0)
                yield break;

            var snapshot = _phaseStartGates.ToArray();
            foreach (var gate in snapshot)
            {
                if (gate == null)
                    continue;

                IEnumerator routine = null;
                try
                {
                    routine = gate(isPlayer);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, this);
                }

                if (routine != null)
                    yield return StartCoroutine(routine);
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
            yield return RunPhaseStartGates(isPlayer);

            float delay = Mathf.Max(1f, Mathf.Max(0f, phaseStartDelaySeconds));
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            _activePhaseUnits = units;
            _activePhaseUnitIndex = 0;
            _activePhaseOverrideIndex = -1;

            while (_activePhaseUnits != null && _activePhaseUnitIndex < _activePhaseUnits.Count)
            {
                var unit = _activePhaseUnits[_activePhaseUnitIndex];
                if (unit == null)
                {
                    _activePhaseUnitIndex++;
                    continue;
                }

                var runtime = EnsureRuntime(unit, isPlayer);
                if (runtime == null)
                {
                    _activePhaseUnitIndex++;
                    continue;
                }

                BeginTurn(runtime);
                yield return new WaitUntil(() => !_waitingForEnd || _activeUnit != unit);

                if (_activePhaseOverrideIndex >= 0)
                {
                    if (_activePhaseUnits != null && _activePhaseUnits.Count > 0)
                        _activePhaseUnitIndex = Mathf.Clamp(_activePhaseOverrideIndex, 0, _activePhaseUnits.Count - 1);
                    else
                        _activePhaseUnitIndex = 0;
                    _activePhaseOverrideIndex = -1;
                }
                else
                {
                    _activePhaseUnitIndex++;
                }
            }

            _activePhaseUnits = null;
            _activePhaseUnitIndex = 0;
            _activePhaseOverrideIndex = -1;

            if (isPlayer)
                OnPlayerSideEnd();
            else
                OnEnemySideEnd();
        }

        void BeginTurn(TurnRuntimeV2 runtime)
        {
            if (runtime == null)
                return;

            if (runtime.DeferredFromIdle)
            {
                ResumeDeferredTurn(runtime);
                return;
            }

            var snapshot = runtime.BeginTurn();
            int turnTime = snapshot.BaseNew;
            runtime.SetActivePhaseIndex(_currentPhaseIndex);
            _activeUnit = runtime.Unit;
            _waitingForEnd = true;
            string unitLabel = FormatUnitLabel(runtime.Unit);
            Debug.Log($"[Turn] Begin T{_currentPhaseIndex}({unitLabel}) TT={turnTime} Prepaid={snapshot.Prepaid} Remain={runtime.RemainingTime} BasePrev={snapshot.BasePrev} RemainPrev={snapshot.RemainPrev} Rebased={snapshot.RemainAfterRebase}", this);
            HandleTurnTimeFloor(runtime, snapshot.BaseNew);
            TurnStarted?.Invoke(runtime.Unit);
            RaiseUnitRuntimeChanged(runtime.Unit);
            // ★ 新增：玩家/敌人任何一方的回合开始时，执行已注册的 TurnStart gates（你的 HandleTurnStartGate 就在这里跑）
            bool skipTurn = HandleFullRoundAtTurnBegin(runtime);
            if (!skipTurn)
            {
                StartCoroutine(RunTurnStartSequence(runtime, _currentPhaseIndex));
                if (!runtime.IsPlayer)
                    StartCoroutine(AutoFinishEnemyTurn(runtime));
            }
        }

        void ResumeDeferredTurn(TurnRuntimeV2 runtime)
        {
            runtime.SetActivePhaseIndex(_currentPhaseIndex);
            _activeUnit = runtime.Unit;
            _waitingForEnd = true;
            TurnStarted?.Invoke(runtime.Unit);
            EnsureIdleLogged(runtime, _currentPhaseIndex);
            runtime.ClearDeferredIdle();
            RaiseUnitRuntimeChanged(runtime.Unit);
        }

        void RequestActivePhaseIteratorRestart(int index)
        {
            if (_activePhaseUnits == null || _activePhaseUnits.Count == 0)
                return;

            _activePhaseOverrideIndex = Mathf.Clamp(index, 0, _activePhaseUnits.Count - 1);
        }

        public bool CanDeferActiveUnit(Unit unit)
        {
            if (unit == null)
                return false;

            if (!_currentPhaseIsPlayer)
                return false;

            if (_activeUnit != unit)
                return false;

            var runtime = EnsureRuntime(unit, true);
            if (runtime == null || !runtime.IsPlayer)
                return false;

            if (!runtime.HasReachedIdle || runtime.HasSpentTimeThisTurn || runtime.HasReorderedThisTurn)
                return false;

            if (!_waitingForEnd)
                return false;

            return true;
        }

        public bool TryDeferActivePlayerUnit(Unit insertAfter)
        {
            var unit = _activeUnit;
            if (!CanDeferActiveUnit(unit))
                return false;

            if (_activePhaseUnits == null || !ReferenceEquals(_activePhaseUnits, _playerUnits))
                return false;

            if (insertAfter == null)
                return false;

            if (ReferenceEquals(unit, insertAfter))
                return false;

            int originalIndex = _playerUnits.IndexOf(unit);
            if (originalIndex < 0)
                return false;

            int afterIndex = _playerUnits.IndexOf(insertAfter);
            if (afterIndex <= originalIndex)
                return false;

            var runtime = EnsureRuntime(unit, true);
            if (runtime == null)
                return false;

            _playerUnits.RemoveAt(originalIndex);
            afterIndex = _playerUnits.IndexOf(insertAfter);
            if (afterIndex < 0)
            {
                _playerUnits.Insert(originalIndex, unit);
                return false;
            }

            int targetIndex = Mathf.Clamp(afterIndex + 1, 0, _playerUnits.Count);
            _playerUnits.Insert(targetIndex, unit);

            runtime.MarkReorderedFromIdle(_currentPhaseIndex);
            runtime.SetActivePhaseIndex(_currentPhaseIndex);

            string unitLabel = FormatUnitLabel(unit);
            string targetLabel = FormatUnitLabel(insertAfter);
            Debug.Log($"[Turn] Reorder T{_currentPhaseIndex}(P1) {unitLabel} -> after {targetLabel}", this);

            TurnOrderChanged?.Invoke(true);
            RaiseUnitRuntimeChanged(unit);

            RequestActivePhaseIteratorRestart(Mathf.Min(originalIndex, _playerUnits.Count - 1));

            _activeUnit = null;
            _waitingForEnd = false;

            return true;
        }

        IEnumerator RunTurnStartSequence(TurnRuntimeV2 runtime, int phaseIndex)
        {
            if (runtime == null)
                yield break;

            yield return RunTurnStartGates(runtime.Unit);

            if (runtime.Unit == null)
                yield break;

            EnsureIdleLogged(runtime, phaseIndex);
        }

        void EnsureIdleLogged(TurnRuntimeV2 runtime, int phaseIndexHint)
        {
            if (runtime == null || runtime.HasReachedIdle)
                return;

            int phaseIndex = phaseIndexHint > 0 ? phaseIndexHint : runtime.ActivePhaseIndex;
            if (phaseIndex <= 0)
                phaseIndex = _currentPhaseIndex;

            string unitLabel = FormatUnitLabel(runtime.Unit);
            Debug.Log($"[Turn] Idle T{phaseIndex}({unitLabel})", this);
            runtime.MarkIdleReached();
        }

        bool HandleFullRoundAtTurnBegin(TurnRuntimeV2 runtime)
        {
            if (runtime == null)
                return false;

            if (!_fullRoundStates.TryGetValue(runtime.Unit, out var state) || state == null)
                return false;

            state.roundsRemaining = Mathf.Max(0, state.roundsRemaining - 1);
            _fullRoundStates[runtime.Unit] = state;

            string unitLabel = FormatUnitLabel(runtime.Unit);
            string phaseLabel = FormatPhaseLabel(runtime.IsPlayer);

            if (state.roundsRemaining > 0)
            {
                Debug.Log($"[FullRound] Skip T{_currentPhaseIndex}({phaseLabel}) U={unitLabel} id={state.actionId} reason=fullround roundsLeft={state.roundsRemaining}", this);
                StartCoroutine(AutoSkipTurn(runtime.Unit));
                return true;
            }

            var plan = state.plan;
            Debug.Log($"[FullRound] ResolveBegin T{_currentPhaseIndex}({phaseLabel}) U={unitLabel} id={state.actionId} roundsTotal={state.totalRounds}", this);

            if (plan.valid)
                ActionPhaseLogger.Log(runtime.Unit, state.actionId, "W3_ExecuteBegin", $"(budgetBefore={plan.budgetBefore}, energyBefore={plan.energyBefore})");
            else
                ActionPhaseLogger.Log(runtime.Unit, state.actionId, "W3_ExecuteBegin", "(deferred)");

            try
            {
                state.tool?.TriggerFullRoundResolution(runtime.Unit, this, plan);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }

            ActionPhaseLogger.Log(runtime.Unit, state.actionId, "W3_ExecuteEnd");

            if (plan.valid)
            {
                int energyAction = plan.TotalEnergy;
                ActionPhaseLogger.Log(runtime.Unit, state.actionId, "W4_ResolveBegin", $"(used={plan.plannedSeconds}, refunded=0, net={plan.NetSeconds}, energyMove={plan.plannedMoveEnergy}, energyAtk={plan.plannedAttackEnergy}, energyAction={energyAction})");
                ActionPhaseLogger.Log(runtime.Unit, state.actionId, "W4_ResolveEnd", $"(budgetAfter={plan.budgetAfter}, energyAfter={plan.energyAfter})");
            }
            else
            {
                ActionPhaseLogger.Log(runtime.Unit, state.actionId, "W4_ResolveBegin", "(deferred)");
                ActionPhaseLogger.Log(runtime.Unit, state.actionId, "W4_ResolveEnd");
            }

            Debug.Log($"[FullRound] ResolveEnd T{_currentPhaseIndex}({phaseLabel}) U={unitLabel} id={state.actionId}", this);
            _fullRoundStates.Remove(runtime.Unit);
            return false;
        }

        IEnumerator AutoSkipTurn(Unit unit)
        {
            if (unit == null)
                yield break;
            yield return null;
            EndTurn(unit);
        }

        IEnumerator AutoFinishEnemyTurn(TurnRuntimeV2 runtime)
        {
            if (runtime == null || runtime.Unit == null)
                yield break;

            while (!runtime.HasReachedIdle)
                yield return null;

            float wait = Mathf.Max(0f, autoTurnEndDelaySeconds);
            if (wait > 0f)
                yield return new WaitForSeconds(wait);

            while (true)
            {
                if (!_waitingForEnd || _activeUnit != runtime.Unit)
                    yield break;

                if (GetAutoTurnEndGuard(runtime.Unit) <= 0)
                    break;

                yield return null;
            }

            if (!_waitingForEnd || _activeUnit != runtime.Unit)
                yield break;

            EndTurn(runtime.Unit);
        }

        void ApplyTimeSpend(TurnRuntimeV2 runtime, int seconds, bool silent = false)
        {
            runtime.SpendTime(seconds);
            if (!silent)
                Debug.Log($"[Time] Spend {FormatUnitLabel(runtime.Unit)} {seconds}s -> Remain={runtime.RemainingTime}", this);
            RaiseUnitRuntimeChanged(runtime?.Unit);
        }

        void ApplyTimeRefund(TurnRuntimeV2 runtime, int seconds, bool silent = false)
        {
            runtime.RefundTime(seconds);
            if (!silent)
                Debug.Log($"[Time] Refund {FormatUnitLabel(runtime.Unit)} {seconds}s -> Remain={runtime.RemainingTime}", this);
            RaiseUnitRuntimeChanged(runtime?.Unit);
        }

        void ModifyResource(TurnRuntimeV2 runtime, string id, int delta, string reason, bool isRefund, bool silent = false)
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
            if (!silent)
            {
                string suffix = string.IsNullOrEmpty(reason) ? string.Empty : $" ({reason})";
                string unitLabel = FormatUnitLabel(runtime.Unit);
                if (isRefund)
                    Debug.Log($"[Res] Refund {unitLabel}:{id} +{Mathf.Abs(delta)} -> {after}/{Mathf.Max(0, maxAfter)}{suffix}", this);
                else
                    Debug.Log($"[Res] Spend {unitLabel}:{id} -{Mathf.Abs(delta)} -> {after}/{Mathf.Max(0, maxAfter)}{suffix}", this);
            }

            RaiseUnitRuntimeChanged(runtime?.Unit);
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

        void RaiseUnitRuntimeChanged(Unit unit)
        {
            if (unit == null)
                return;
            UnitRuntimeChanged?.Invoke(unit);
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

            HandleTurnTimeFloor(runtime, runtime.BaseTimeForNext);
            return runtime;
        }
        IEnumerator RunTurnStartGates(Unit unit)
        {
            if (_turnStartGates.Count == 0) yield break;

            var snapshot = _turnStartGates.ToArray();
            foreach (var gate in snapshot)
            {
                if (gate == null) continue;

                IEnumerator routine = null;
                try { routine = gate(unit); }
                catch (Exception ex) { Debug.LogException(ex, this); }

                if (routine != null)
                    yield return StartCoroutine(routine);
            }
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

        public static string FormatUnitLabel(Unit unit)
        {
            if (unit == null)
                return "?";
            if (!string.IsNullOrEmpty(unit.Id))
                return unit.Id;
            return "?";
        }

        static string FormatPhaseLabel(bool isPlayer) => isPlayer ? "Player" : "Enemy";

        void RefreshPhaseBeginUnits(bool isPlayerPhase)
        {
            if (isPlayerPhase)
            {
                RefreshSideUnits(_playerUnits, true);
                RefreshSideUnits(_enemyUnits, false);
            }
            else
            {
                RefreshSideUnits(_enemyUnits, false);
                RefreshSideUnits(_playerUnits, true);
            }
        }

        void OnPlayerPhaseBegin()
        {
            PlayerPhaseStarted?.Invoke();
            PhaseBegan?.Invoke(true);
            RefreshPhaseBeginUnits(true);
        }

        void OnEnemyPhaseBegin()
        {
            EnemyPhaseStarted?.Invoke();
            PhaseBegan?.Invoke(false);
            RefreshPhaseBeginUnits(false);
        }

        void OnPlayerSideEnd()
        {
            string phaseLabel = FormatPhaseLabel(true);
            Debug.Log($"[Phase] End T{_currentPhaseIndex}({phaseLabel})", this);
            ApplySideEndTicks(true);
            ResetSideBudgets(true);
            ClearTempAttackLayer("SideEnd");
            PlayerSideEnded?.Invoke();
            SideEnded?.Invoke(true);
        }

        void OnEnemySideEnd()
        {
            string phaseLabel = FormatPhaseLabel(false);
            Debug.Log($"[Phase] End T{_currentPhaseIndex}({phaseLabel})", this);
            ApplySideEndTicks(false);
            ResetSideBudgets(false);
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

        void ResetSideBudgets(bool isPlayerSide)
        {
            foreach (var pair in _runtimeByUnit)
            {
                var runtime = pair.Value;
                if (runtime == null || runtime.IsPlayer != isPlayerSide)
                    continue;

                int before = runtime.RemainingTime;
                int basePrev = runtime.BaseTimeForNext;
                int baseNew = runtime.ResetBudget();
                string unitLabel = FormatUnitLabel(runtime.Unit);
                string phaseLabel = FormatPhaseLabel(isPlayerSide);
                Debug.Log($"[Time] Reset T{_currentPhaseIndex}({phaseLabel}) U={unitLabel} {before}s -> {runtime.RemainingTime}s basePrev={basePrev} baseNew={baseNew}", this);
                HandleTurnTimeFloor(runtime, baseNew);
            }
        }

        void HandleTurnTimeFloor(TurnRuntimeV2 runtime, int baseSeconds)
        {
            if (runtime == null || runtime.Unit == null)
                return;

            if (baseSeconds <= 0)
            {
                if (_turnTimeDepletedUnits.Add(runtime.Unit))
                {
                    int speed = runtime.Context != null ? runtime.Context.Speed : 0;
                    string unitLabel = FormatUnitLabel(runtime.Unit);
                    Debug.LogWarning($"[Turn] BaseTimeZero U={unitLabel} speed={speed} (hook pending)", this);
                    UnitTurnTimeDepleted?.Invoke(runtime.Unit);
                }
            }
            else if (_turnTimeDepletedUnits.Remove(runtime.Unit))
            {
                string unitLabel = FormatUnitLabel(runtime.Unit);
                Debug.Log($"[Turn] BaseTimeRestore U={unitLabel} base={baseSeconds}", this);
            }
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
                float combinedMult = Mathf.Clamp(context.MoveRates.NormalizedMultiplier * product, 0.01f, 100f);
                float recomputed = StatsMathV2.MR_MultiThenFlat(
                    context.BaseMoveRate,
                    new[] { combinedMult },
                    context.MoveRateFlatAdd,
                    context.MoveRateMin,
                    context.MoveRateMax);
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

            int defaultDelta = -StatsMathV2.BaseTurnSeconds;
            int delta = defaultDelta;
            var context = runtime.Context;
            var set = context != null ? context.Rules : null;
            var rulesCtx = RulesAdapter.BuildContext(
                context,
                actionId: null,
                kind: ActionKind.Free,
                chainDepth: 0,
                comboIndex: 0,
                planSecs: 0,
                planEnergy: 0
            );
            RuleEngineV2.Instance.OnTickCooldown(set, in rulesCtx, ref delta);
            if (delta != defaultDelta)
                ActionPhaseLogger.Log($"[Rules] CD tick: {defaultDelta}->{delta} (TickMods)");

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
                int after = store.AddSeconds(skillId, delta);
                if (after < 0)
                {
                    store.StartSeconds(skillId, 0);
                    after = 0;
                }
                int turns = store.TurnsLeft(skillId);
                details.Add($"{skillId}:{before}->{after} (turns={turns})");
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
                RaiseUnitRuntimeChanged(runtime?.Unit);
                return;
            }

            Debug.Log($"[Res]   Regen  T{_currentPhaseIndex}({phaseLabel}) U={unitLabel} +{regen.gain} -> {regen.current}/{regen.max} (EndTurnRegen)", this);
            RaiseUnitRuntimeChanged(runtime?.Unit);
        }

        public bool IsPlayerUnit(Unit unit) => unit != null && _playerUnits.Contains(unit);
        public bool IsEnemyUnit(Unit unit) => unit != null && _enemyUnits.Contains(unit);
    }
}