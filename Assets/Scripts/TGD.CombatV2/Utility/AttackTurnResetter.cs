using UnityEngine;
using TGD.HexBoard;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    public sealed class AttackTurnResetter : MonoBehaviour
    {
        public TurnManagerV2 tm;
        void OnEnable()
        {
            if (tm != null)
                tm.TurnStarted += OnTurnStarted;
        }

        void OnDisable()
        {
            if (tm != null)
                tm.TurnStarted -= OnTurnStarted;
        }
        void OnTurnStarted(Unit u)
        {
            if (tm == null || u == null)
                return;

            UnitRuntimeContext context = tm.GetContext(u);
            var adapter = context != null
                ? context.GetComponent<AttackCostServiceV2Adapter>()
                : null;
            adapter?.ResetForNewTurn();
        }
    }
}