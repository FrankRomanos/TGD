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
        void Awake()
        {
            Apply();
        }


        [ContextMenu("Apply Wiring")]
        public void Apply()
        {
            if (context == null)
                context = GetComponent<UnitRuntimeContext>();

            WireMoveCosts();
            WireAttackCosts();
            WireStatusRuntime();
            AutoWireHudAndAnim();

            Debug.Log($"[UnitAutoWire] Applied on {name}", this);
        }

        void WireMoveCosts()
        {
            var move = GetComponent<MoveCostServiceV2Adapter>();
            if (move == null)
                return;

            move.turnManager = turnManager;
            move.ctx = context;
            if (context != null && move.cooldownHub == null)
                move.cooldownHub = context.cooldownHub;
            if (context != null && move.stats == null)
                move.stats = context.stats;
        }

        void WireAttackCosts()
        {
            var atk = GetComponent<AttackCostServiceV2Adapter>();
            if (atk == null)
                return;

            atk.turnManager = turnManager;
            atk.ctx = context;
            if (context != null && atk.cooldownHub == null)
                atk.cooldownHub = context.cooldownHub;
            if (context != null && atk.stats == null)
                atk.stats = context.stats;
        }

        void WireStatusRuntime()
        {
            var driver = ResolveDriver();
            var statuses = GetComponentsInChildren<MoveRateStatusRuntime>(true);
            foreach (var status in statuses)
            {
                if (status == null)
                    continue;

                if (turnManager != null)
                    status.AttachTurnManager(turnManager);
                if (driver != null)
                    status.AttachDriver(driver);
            }
        }

        void AutoWireHudAndAnim()
        {
            var driver = ResolveDriver();
            if (driver == null)
                return;

            var moveHuds = GetComponentsInChildren<MoveHudListenerTMP>(true);
            foreach (var hud in moveHuds)
            {
                if (hud != null && hud.driver == null)
                    hud.driver = driver;
            }

            var attackHuds = GetComponentsInChildren<AttackHudListenerTMP>(true);
            foreach (var hud in attackHuds)
            {
                if (hud != null && hud.driver == null)
                    hud.driver = driver;
            }

            var animDrivers = GetComponentsInChildren<AttackAnimDriver>(true);
            foreach (var anim in animDrivers)
            {
                if (anim != null && anim.driver == null)
                    anim.driver = driver;
            }

            var moveAnimListeners = GetComponentsInChildren<AttackMoveAnimListener>(true);
            foreach (var listener in moveAnimListeners)
            {
                if (listener != null && listener.driver == null)
                    listener.driver = driver;
            }
        }

        HexBoardTestDriver ResolveDriver()
            => GetComponentInParent<HexBoardTestDriver>();
    }
}
