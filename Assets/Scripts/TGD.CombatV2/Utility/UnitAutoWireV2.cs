// File: TGD.CombatV2/Utility/UnitAutoWireV2.cs
using UnityEngine;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    /// <summary>
    /// 可选：把同物体上的 Adapter 接上 TurnManager 与 Context。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitAutoWireV2 : MonoBehaviour
    {
        public TurnManagerV2 turnManager;
        public UnitRuntimeContext context;

        TurnManagerV2 _registeredTurnManager;
        UnitRuntimeContext _registeredContext;
        CooldownStoreSecV2 _registeredCooldownStore;
        void Awake()
        {
            Apply();
        }


        [ContextMenu("Apply Wiring")]
        public void Apply()
        {
            if (context == null)
                context = GetComponent<UnitRuntimeContext>();
            bool changed = RegisterCoreBindings();
            changed |= WireAttackCosts();
            changed |= WireStatusRuntime();
            changed |= AutoWireHudAndAnim();

            if (changed && turnManager != null)
                Debug.Log($"[UnitAutoWire] Applied on {name}", this);
        }

        bool RegisterCoreBindings()
        {
            bool changed = false;

            if (turnManager == null || context == null)
            {
                _registeredTurnManager = null;
                _registeredContext = null;
                _registeredCooldownStore = null;
                return false;
            }

            if (_registeredTurnManager != turnManager || _registeredContext != context)
            {
                turnManager.RegisterContext(context);
                changed = true;
            }

            var cooldownStore = context.cooldownHub != null ? context.cooldownHub.secStore : null;
            if (cooldownStore != null)
            {
                if (_registeredTurnManager != turnManager || _registeredCooldownStore != cooldownStore)
                {
                    turnManager.RegisterCooldownStore(cooldownStore);
                    changed = true;
                }
            }

            _registeredTurnManager = turnManager;
            _registeredContext = context;
            _registeredCooldownStore = cooldownStore;

            return changed;
        }

        bool WireAttackCosts()
        {
#if UNITY_EDITOR
            // 如不想看提示，删掉这行即可
#endif
            return false;
        }

        bool WireStatusRuntime()
        {
            var statuses = GetComponentsInChildren<MoveRateStatusRuntime>(true);
            bool changed = false;
            foreach (var status in statuses)
            {
                if (status == null)
                    continue;

                if (status.ctx != context || status.turnManager != turnManager)
                {
                    status.Attach(context, turnManager);
                    changed = true;
                }
            }

            return changed;
        }

        bool AutoWireHudAndAnim()
        {
            var driver = ResolveDriver();
            bool changed = false;

            var hudListeners = GetComponentsInChildren<ActionHudMessageListenerTMP>(true);
            foreach (var hud in hudListeners)
            {
                if (hud == null)
                    continue;

                if (driver != null && hud.driver == null)
                {
                    hud.driver = driver;
                    changed = true;
                }
            }

            var animDrivers = GetComponentsInChildren<AttackAnimDriver>(true);
            foreach (var anim in animDrivers)
            {
                if (anim == null)
                    continue;

                if (context != null && anim.ctx == null)
                {
                    anim.ctx = context;
                    changed = true;
                }

                if (driver != null && driver.UnitRef != null)
                {
                    anim.BindUnit(driver.UnitRef);
                }
            }

            var moveAnimListeners = GetComponentsInChildren<AttackMoveAnimListener>(true);
            foreach (var listener in moveAnimListeners)
            {
                if (listener == null)
                    continue;

                if (context != null && listener.ctx == null)
                {
                    listener.ctx = context;
                    changed = true;
                }
            }

            return changed;
        }

        HexBoardTestDriver ResolveDriver()
            => GetComponentInParent<HexBoardTestDriver>();
    }
}
