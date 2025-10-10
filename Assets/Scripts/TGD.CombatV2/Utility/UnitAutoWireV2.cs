// File: TGD.CombatV2/Utility/UnitAutoWireV2.cs
using UnityEngine;
using TGD.CoreV2;

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

        [ContextMenu("Apply Wiring")]
        public void Apply()
        {
            if (context == null) context = GetComponent<UnitRuntimeContext>();

            // Move
            var move = GetComponent<MoveCostServiceV2Adapter>();
            if (move != null)
            {
                move.turnManager = turnManager;
                move.ctx = context;
                if (context != null && move.cooldownHub == null)
                    move.cooldownHub = context.cooldownHub;
                if (context != null && move.stats == null)
                    move.stats = context.stats;
            }

            // Attack
            var atk = GetComponent<AttackCostServiceV2Adapter>();
            if (atk != null)
            {
                atk.turnManager = turnManager;
                atk.ctx = context;
                if (context != null && atk.cooldownHub == null)
                    atk.cooldownHub = context.cooldownHub;
                if (context != null && atk.stats == null)
                    atk.stats = context.stats;
            }

            Debug.Log($"[UnitAutoWire] Applied on {name}", this);
        }
    }
}
