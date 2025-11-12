// File: TGD.HexBoard/HexHazardWatcher.cs
using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    public interface IHexEntangleResponder
    {
        bool TryApplyEntangle(
            UnitRuntimeContext context,
            Unit unit,
            Hex hex,
            HazardType hazard,
            string tag,
            int turns);
    }

    /// 监听单位格位变化：踩陷阱打印；落穴阻挡在预览阶段由 ClickMover 的 Block + env.IsPit 负责
    [DisallowMultipleComponent]
    public sealed class HexHazardWatcher : MonoBehaviour
    {
        [HideInInspector]
        public UnitRuntimeContext ctx;
        [HideInInspector]
        public HexEnvironmentSystem env;

        Hex _last;
        bool _has;
        readonly HashSet<HazardType> _activeEntangleHazards = new();
        readonly List<HazardType> _entangleScratch = new();
        readonly List<HazardType> _entangleToRemove = new();
        IHexEntangleResponder _cachedEntangleResponder;
        Component _cachedEntangleResponderComponent;

        static readonly List<HexHazardWatcher> s_WatcherScratch = new();
        static readonly HashSet<HexHazardWatcher> s_WatcherUnique = new();

        public void Attach(UnitRuntimeContext context, HexEnvironmentSystem environment)
        {
            ctx = context != null ? context : ResolveContext();
            env = environment != null ? environment : ResolveEnvironment();
            ResetCache();
        }

        public void RefreshFactoryInjection(
            UnitRuntimeContext context = null,
            HexEnvironmentSystem environment = null)
        {
            Attach(
                context != null ? context : ctx,
                environment != null ? environment : env);
        }

        void OnEnable()
        {
            ResetCache();
        }

        void OnDisable()
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
            ClearEntangleResponderCache();
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
                HandleTraversal(unit, cur, environment);
        }

        public void HandleTraversal(Unit unit, Hex hex, HexEnvironmentSystem environment = null)
        {
            _last = hex;
            _has = true;

            unit ??= ResolveUnit();
            environment ??= ResolveEnvironment();
            if (environment == null || unit == null)
                return;

            EmitHazardEnterLogs(environment, unit, hex);
            ApplyHazardEnterEffects(environment, unit, hex);
        }

        public void HandleTraversal(Hex hex, HexEnvironmentSystem environment = null)
        {
            HandleTraversal(null, hex, environment);
        }

        public static void NotifyTraversal(UnitRuntimeContext context, Unit unit, Hex hex, HexEnvironmentSystem environment = null)
        {
            if (context == null)
                return;

            s_WatcherScratch.Clear();
            s_WatcherUnique.Clear();

            context.GetComponentsInChildren(true, s_WatcherScratch);

            if (s_WatcherScratch.Count == 0)
                return;

            unit ??= context.boundUnit;
            try
            {
                for (int i = 0; i < s_WatcherScratch.Count; i++)
                {
                    var watcher = s_WatcherScratch[i];
                    if (watcher != null && s_WatcherUnique.Add(watcher))
                        watcher.HandleTraversal(unit, hex, environment);
                }
            }
            finally
            {
                s_WatcherScratch.Clear();
                s_WatcherUnique.Clear();
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

            bool applied = false;
            int turns = Mathf.Max(1, hazard.entangleDurationTurns);
            string tag = BuildEntangleTag(hazard, hex);

            var responder = ResolveEntangleResponder();
            if (responder != null)
                applied = responder.TryApplyEntangle(context, unit, hex, hazard, tag, turns);

            if (!applied && context != null && !context.MoveRates.IsEntangled)
            {
                context.MoveRates.SetEntangled(true);
                applied = true;
                Debug.LogWarning($"[Snare] Applied entangle via fallback (missing entangle responder) at {hex}", this);
            }

            if (!applied)
                return false;

            if (hazard.destroyAfterEntangleTrigger && environment != null && environment.envMap != null)
            {
                environment.envMap.RemoveAll(hazard);
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

        void ClearEntangleResponderCache()
        {
            _cachedEntangleResponder = null;
            _cachedEntangleResponderComponent = null;
        }

        IHexEntangleResponder ResolveEntangleResponder()
        {
            if (_cachedEntangleResponderComponent != null)
            {
                if (_cachedEntangleResponderComponent)
                    return _cachedEntangleResponder;

                ClearEntangleResponderCache();
            }

            var context = ResolveContext();
            if (context != null)
            {
                if (TryCacheResponder(context.GetComponent<IHexEntangleResponder>()))
                    return _cachedEntangleResponder;
                if (TryCacheResponder(context.GetComponentInParent<IHexEntangleResponder>(true)))
                    return _cachedEntangleResponder;
                if (TryCacheResponder(context.GetComponentInChildren<IHexEntangleResponder>(true)))
                    return _cachedEntangleResponder;
            }

            if (TryCacheResponder(GetComponent<IHexEntangleResponder>()))
                return _cachedEntangleResponder;
            if (TryCacheResponder(GetComponentInParent<IHexEntangleResponder>(true)))
                return _cachedEntangleResponder;
            if (TryCacheResponder(GetComponentInChildren<IHexEntangleResponder>(true)))
                return _cachedEntangleResponder;

            return null;
        }

        bool TryCacheResponder(IHexEntangleResponder responder)
        {
            if (responder == null)
                return false;

            if (responder is Component component)
            {
                _cachedEntangleResponder = responder;
                _cachedEntangleResponderComponent = component;
                return true;
            }

            return false;
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
    }
}
