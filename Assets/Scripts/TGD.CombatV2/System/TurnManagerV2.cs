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
        public event Action<Unit> TurnStarted;
        public event Action<Unit> TurnEnded;

        readonly List<Unit> _playerUnits = new();
        readonly List<Unit> _enemyUnits = new();

        readonly Dictionary<Unit, UnitRuntimeContext> _contextByUnit = new();
        readonly Dictionary<Unit, TurnRuntimeV2> _runtimeByUnit = new();
        readonly Dictionary<Unit, ITurnBudget> _budgetHandles = new();
        readonly Dictionary<Unit, IResourcePool> _resourceHandles = new();
        readonly Dictionary<Unit, ICooldownSink> _cooldownHandles = new();

        Coroutine _loop;
        Unit _activeUnit;
        bool _waitingForEnd;

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

        public void EndTurn(Unit unit)
        {
            if (unit == null) return;
            var runtime = EnsureRuntime(unit, null);
            if (runtime == null) return;

            HandleEndTurn(runtime);
            TurnEnded?.Invoke(unit);
            Debug.Log($"[Turn] EndTurn {FormatUnit(runtime)}", this);

            if (_activeUnit == unit)
            {
                _activeUnit = null;
                _waitingForEnd = false;
            }
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
            if (isPlayer)
            {
                Debug.Log("[Turn] PlayerPhaseStart", this);
                PlayerPhaseStarted?.Invoke();
            }
            else
            {
                Debug.Log("[Turn] EnemyPhaseStart", this);
                EnemyPhaseStarted?.Invoke();
            }

            float delay = Mathf.Max(1f, phaseStartDelaySeconds);
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            foreach (var unit in units)
            {
                if (unit == null) continue;
                var runtime = EnsureRuntime(unit, isPlayer);
                if (runtime == null) continue;

                BeginTurn(runtime);
                yield return new WaitUntil(() => !_waitingForEnd || _activeUnit != unit);
            }
        }

        void BeginTurn(TurnRuntimeV2 runtime)
        {
            runtime.ResetBudget();
            _activeUnit = runtime.Unit;
            _waitingForEnd = true;
            Debug.Log($"[Turn] StartTurn {FormatUnit(runtime)} TT={runtime.TurnTime} Prepaid={runtime.PrepaidTime} Remain={runtime.RemainingTime}", this);
            TurnStarted?.Invoke(runtime.Unit);
        }

        void ApplyTimeSpend(TurnRuntimeV2 runtime, int seconds)
        {
            runtime.SpendTime(seconds);
            Debug.Log($"[Time] Spend {FormatUnit(runtime)} {seconds}s -> Remain={runtime.RemainingTime}", this);
        }

        void ApplyTimeRefund(TurnRuntimeV2 runtime, int seconds)
        {
            runtime.RefundTime(seconds);
            Debug.Log($"[Time] Refund {FormatUnit(runtime)} {seconds}s -> Remain={runtime.RemainingTime}", this);
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
            if (isRefund)
                Debug.Log($"[Res] Refund {FormatUnit(runtime)}:{id} +{Mathf.Abs(delta)} -> {after}/{Mathf.Max(0, maxAfter)}{suffix}", this);
            else
                Debug.Log($"[Res] Spend {FormatUnit(runtime)}:{id} -{Mathf.Abs(delta)} -> {after}/{Mathf.Max(0, maxAfter)}{suffix}", this);
        }

        void HandleEndTurn(TurnRuntimeV2 runtime)
        {
            TickCooldowns(runtime);
            HandleEnergyRegen(runtime);
            Debug.Log($"[Buff] Tick {FormatUnit(runtime)} -1 turn (placeholder)", this);
            runtime.ClearPrepaid();
        }

        void TickCooldowns(TurnRuntimeV2 runtime)
        {
            var store = GetSecStore(runtime);
            if (store == null) return;
            var keys = store.Keys.ToList();
            foreach (var key in keys)
            {
                int left = store.AddSeconds(key, -StatsMathV2.BaseTurnSeconds);
                int turns = store.TurnsLeft(key);
                Debug.Log($"[CD] TickEndTurn {FormatUnit(runtime)}:{key} -{StatsMathV2.BaseTurnSeconds}s -> {left}s (turns={turns})", this);
            }
        }

        void HandleEnergyRegen(TurnRuntimeV2 runtime)
        {
            var ctx = runtime.Context;
            var stats = ctx != null ? ctx.stats : null;
            if (stats == null)
            {
                Debug.Log($"[Res] Refund {FormatUnit(runtime)}:Energy +0 -> 0/0 (EndTurnRegen)", this);
                return;
            }

            int turnTime = runtime.TurnTime;
            float chunks = Mathf.Max(0, turnTime) / 2f;
            int gain = Mathf.FloorToInt(chunks * Mathf.Max(0, stats.EnergyRegenPer2s));
            int max = Mathf.Max(0, stats.MaxEnergy);
            int before = stats.Energy;
            stats.Energy = Mathf.Clamp(before + gain, 0, max);
            Debug.Log($"[Res] Refund {FormatUnit(runtime)}:Energy +{gain} -> {stats.Energy}/{max} (EndTurnRegen)", this);
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
            Debug.Log($"[CD] StartSeconds {FormatUnit(runtime)}:{skillId} = {seconds}s (turns={turns})", this);
        }

        void LogCooldownAdd(TurnRuntimeV2 runtime, string skillId, int delta, int left, int turns)
        {
            Debug.Log($"[CD] AddSeconds {FormatUnit(runtime)}:{skillId} += {delta}s -> {left}s (turns={turns})", this);
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
            }
            else if (isPlayerHint.HasValue && runtime.IsPlayer != isPlayerHint.Value)
            {
                // nothing for now, runtime tracks initial flag
            }

            if (_contextByUnit.TryGetValue(unit, out var context) && runtime.Context != context)
                runtime.Bind(context);

            return runtime;
        }

        static bool IsEnergy(string id) => string.Equals(id, "Energy", StringComparison.OrdinalIgnoreCase);

        static string FormatUnit(TurnRuntimeV2 runtime)
        {
            var unit = runtime?.Unit;
            return unit != null ? unit.Id : "<null>";
        }
    }
}
