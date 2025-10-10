using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public sealed class AttackTurnResetter : MonoBehaviour
    {
        public TurnManagerV2 tm;
        void OnEnable() { if (tm) tm.TurnStarted += OnTurnStarted; }
        void OnDisable() { if (tm) tm.TurnStarted -= OnTurnStarted; }

        void OnTurnStarted(Unit u)
        {
            if (!u) return;
            var adapter = u.GetComponent<AttackCostServiceV2Adapter>();
            adapter?.ResetForNewTurn();
        }
    }
}