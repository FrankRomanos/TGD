// File: TGD.HexBoard/HexHazardWatcher.cs
using System.Collections.Generic;
using TGD.CombatV2;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    /// 监听单位格位变化：踩陷阱打印；落穴阻挡在预览阶段由 ClickMover 的 Block + env.IsPit 负责
    [DisallowMultipleComponent]
    public sealed class HexHazardWatcher : MonoBehaviour
    {
        [HideInInspector]
        public UnitRuntimeContext ctx;
        [HideInInspector]
        public HexEnvironmentSystem env;
        [HideInInspector]
        public MoveRateStatusRuntime status;

        Hex _last;
        bool _has;
        readonly HashSet<HazardType> _activeEntangleHazards = new();
        readonly List<HazardType> _entangleScratch = new();
        readonly List<HazardType> _entangleToRemove = new();

        public void Attach(UnitRuntimeContext context, HexEnvironmentSystem environment, MoveRateStatusRuntime moveStatus = null)
        {
            ctx = context != null ? context : ResolveContext();
            env = environment != null ? environment : ResolveEnvironment();
            status = moveStatus != null ? moveStatus : ResolveStatus();
            ResetCache();
        }

        public void RefreshFactoryInjection(
            UnitRuntimeContext context = null,
            HexEnvironmentSystem environment = null,
            MoveRateStatusRuntime moveStatus = null)
        {
            Attach(
                context != null ? context : ctx,
                environment != null ? environment : env,
                moveStatus != null ? moveStatus : status);
        }

        void OnEnable()
        {
            ResetCache();
        }

        void ResetCache()
        {
            _has = false;
            _last = Hex.Zero;
            _activeEntangleHazards.Clear();
            _entangleScratch.Clear();
            _entangleToRemove.Clear();
        }

        void LateUpdate()
        {
            var unit = ResolveUnit();
            var environment = ResolveEnvironment();
            if (unit == null || environment == null)
                return;

            var cur = unit.Position;
            if (!_has)
            {
                _last = cur;
                _has = true;
                return;
            }

            if (!cur.Equals(_last))
            {
                EmitHazardEnterLogs(environment, unit, cur);
                ApplyHazardEnterEffects(environment, unit, cur);
                _last = cur;
            }
        }

        void EmitHazardEnterLogs(HexEnvironmentSystem environment, Unit unit, Hex hex)
        {
            var envMap = environment != null ? environment.envMap : null;
            bool logged = false;

            if (envMap != null)
            {
                var effects = envMap.Get(hex);
                for (int i = 0; i < effects.Count; i++)
                {
                    var hazard = effects[i].Hazard;
                    if (hazard == null)
                        continue;

                    hazard.EmitEnterLog(unit, hex);
                    logged = true;
                }
            }

            if (!logged && environment != null && environment.IsTrap(hex))
            {
                Debug.Log($"[Trap] {unit} stepped on TRAP at {hex} → take damage (legacy log)");
            }
        }

        void ApplyHazardEnterEffects(HexEnvironmentSystem environment, Unit unit, Hex hex)
        {
            var envMap = environment != null ? environment.envMap : null;
            _entangleScratch.Clear();

            if (envMap != null)
            {
                var effects = envMap.Get(hex);
                for (int i = 0; i < effects.Count; i++)
                {
                    var hazard = effects[i].Hazard;
                    if (hazard == null || hazard.kind != HazardKind.EntangleTrap)
                        continue;
                    _entangleScratch.Add(hazard);
                }
            }

            if (_entangleScratch.Count == 0)
            {
                if (_activeEntangleHazards.Count > 0)
                    _activeEntangleHazards.Clear();
                return;
            }

            _entangleToRemove.Clear();
            foreach (var active in _activeEntangleHazards)
            {
                if (!_entangleScratch.Contains(active))
                    _entangleToRemove.Add(active);
            }

            for (int i = 0; i < _entangleToRemove.Count; i++)
                _activeEntangleHazards.Remove(_entangleToRemove[i]);
            _entangleToRemove.Clear();

            for (int i = 0; i < _entangleScratch.Count; i++)
            {
                var hazard = _entangleScratch[i];
                if (hazard == null)
                    continue;
                if (_activeEntangleHazards.Contains(hazard))
                    continue;

                bool keepActive = TryApplyEntangle(environment, unit, hex, hazard);
                if (keepActive)
                    _activeEntangleHazards.Add(hazard);
            }
        }

        bool TryApplyEntangle(HexEnvironmentSystem environment, Unit unit, Hex hex, HazardType hazard)
        {
            if (hazard == null)
                return false;

            var context = ResolveContext();
            var moveStatus = ResolveStatus();

            bool applied = false;
            int turns = Mathf.Max(1, hazard.entangleDurationTurns);
            string tag = BuildEntangleTag(hazard, hex);

            if (moveStatus != null)
            {
                applied = moveStatus.ApplyEntangle(tag, turns, hazard?.name);
            }
            else if (context != null && !context.MoveRates.IsEntangled)
            {
                context.MoveRates.SetEntangled(true);
                applied = true;
                Debug.LogWarning($"[Snare] Applied entangle via fallback (missing MoveRateStatusRuntime) at {hex}", this);
            }

            if (!applied)
                return false;

            if (hazard.destroyAfterEntangleTrigger && environment != null && environment.envMap != null)
            {
                environment.envMap.Remove(hex, hazard);
                return false;
            }

            return true;
        }

        static string BuildEntangleTag(HazardType hazard, Hex at)
        {
            if (hazard == null)
                return $"Entangle@{at.q},{at.r}";

            string prefix = !string.IsNullOrEmpty(hazard.hazardId)
                ? hazard.hazardId
                : (!string.IsNullOrEmpty(hazard.name) ? hazard.name : hazard.kind.ToString());

            return $"Entangle@{prefix}@{at.q},{at.r}";
        }

        Unit ResolveUnit()
        {
            var context = ResolveContext();
            if (context != null && context.boundUnit != null)
                return context.boundUnit;

            var adapter = GetComponent<UnitGridAdapter>() ?? GetComponentInParent<UnitGridAdapter>(true);
            if (adapter != null)
                return adapter.Unit;

            return null;
        }

        UnitRuntimeContext ResolveContext()
        {
            if (ctx != null)
                return ctx;

            ctx = GetComponent<UnitRuntimeContext>() ?? GetComponentInParent<UnitRuntimeContext>(true);
            return ctx;
        }

        HexEnvironmentSystem ResolveEnvironment()
        {
            if (env != null)
                return env;

            env = GetComponent<HexEnvironmentSystem>()
                  ?? GetComponentInParent<HexEnvironmentSystem>(true)
                  ?? HexEnvironmentSystem.FindInScene();
            return env;
        }

        MoveRateStatusRuntime ResolveStatus()
        {
            if (status != null)
                return status;

            status = GetComponent<MoveRateStatusRuntime>()
                     ?? GetComponentInParent<MoveRateStatusRuntime>(true)
                     ?? GetComponentInChildren<MoveRateStatusRuntime>(true);

            if (status == null && ctx != null)
            {
                status = ctx.GetComponent<MoveRateStatusRuntime>()
                         ?? ctx.GetComponentInParent<MoveRateStatusRuntime>(true)
                         ?? ctx.GetComponentInChildren<MoveRateStatusRuntime>(true);
            }

            return status;
        }
    }
}
