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
            if (context != null)
                ctx = context;
            env = environment != null ? environment : env;
            ResetCache();
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
                // 进入新格：陷阱触发（可重复，每次进入都打印）
                if (environment.IsTrap(cur))
                {
                    Debug.Log($"[Trap] {unit} stepped on TRAP at {cur} → take damage (test log)");
                }
                _last = cur;
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

            env = GetComponent<HexEnvironmentSystem>() ?? GetComponentInParent<HexEnvironmentSystem>(true);
            return env;
        }
    }
}
