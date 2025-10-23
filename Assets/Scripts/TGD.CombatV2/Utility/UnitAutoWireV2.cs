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
            changed |= WireMoveCosts();
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

        bool WireMoveCosts()
        {
            var move = GetComponent<MoveCostServiceV2Adapter>();
            if (move == null)
                return false;

            bool changed = false;

            if (move.turnManager != turnManager)
            {
                move.turnManager = turnManager;
                changed = true;
            }

            if (move.ctx != context)
            {
                move.ctx = context;
                changed = true;
            }

            if (context != null)
            {
                if (move.cooldownHub == null && context.cooldownHub != null)
                {
                    move.cooldownHub = context.cooldownHub;
                    changed = true;
                }

                if (move.stats == null && context.stats != null)
                {
                    move.stats = context.stats;
                    changed = true;
                }
            }

            return changed;
        }

        bool WireAttackCosts()
        {
            var atk = GetComponent<AttackCostServiceV2Adapter>();
            if (atk == null)
                return false;

            bool changed = false;

            if (atk.turnManager != turnManager)
            {
                atk.turnManager = turnManager;
                changed = true;
            }

            if (atk.ctx != context)
            {
                atk.ctx = context;
                changed = true;
            }

            if (context != null)
            {
                if (atk.cooldownHub == null && context.cooldownHub != null)
                {
                    atk.cooldownHub = context.cooldownHub;
                    changed = true;
                }

                if (atk.stats == null && context.stats != null)
                {
                    atk.stats = context.stats;
                    changed = true;
                }
            }

            return changed;
        }

        bool WireStatusRuntime()
        {
            var driver = ResolveDriver();
            var statuses = GetComponentsInChildren<MoveRateStatusRuntime>(true);
            bool changed = false;
            foreach (var status in statuses)
            {
                if (status == null)
                    continue;

                if (turnManager != null && status.turnManager != turnManager)
                {
                    status.AttachTurnManager(turnManager);
                    changed = true;
                }

                if (driver != null && status.driver != driver)
                {
                    status.AttachDriver(driver);
                    changed = true;
                }
            }

            return changed;
        }

        bool AutoWireHudAndAnim()
        {
            var driver = ResolveDriver();
            if (driver == null)
                return false;

            bool changed = false;

            var hudListeners = GetComponentsInChildren<ActionHudMessageListenerTMP>(true);
            foreach (var hud in hudListeners)
            {
                if (hud != null && hud.driver == null)
                {
                    hud.driver = driver;
                    changed = true;
                }
            }

            var animDrivers = GetComponentsInChildren<AttackAnimDriver>(true);
            foreach (var anim in animDrivers)
            {
                if (anim != null && anim.driver == null)
                {
                    anim.driver = driver;
                    changed = true;
                }
            }

            var moveAnimListeners = GetComponentsInChildren<AttackMoveAnimListener>(true);
            foreach (var listener in moveAnimListeners)
            {
                if (listener != null && listener.driver == null)
                {
                    listener.driver = driver;
                    changed = true;
                }
            }

            return changed;
        }

        HexBoardTestDriver ResolveDriver()
            => GetComponentInParent<HexBoardTestDriver>();
    }
}
