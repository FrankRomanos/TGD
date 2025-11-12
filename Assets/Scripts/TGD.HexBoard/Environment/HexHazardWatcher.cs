// File: TGD.HexBoard/HexHazardWatcher.cs
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

        Hex _last;
        bool _has;

        public void Attach(UnitRuntimeContext context, HexEnvironmentSystem environment)
        {
            ctx = context != null ? context : ResolveContext();
            env = environment != null ? environment : ResolveEnvironment();
            ResetCache();
        }

        public void RefreshFactoryInjection(UnitRuntimeContext context = null, HexEnvironmentSystem environment = null)
        {
            Attach(context != null ? context : ctx, environment != null ? environment : env);
        }

        void OnEnable()
        {
            ResetCache();
        }

        void ResetCache()
        {
            _has = false;
            _last = Hex.Zero;
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
