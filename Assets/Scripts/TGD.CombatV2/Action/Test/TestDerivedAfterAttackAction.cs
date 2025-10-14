using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public sealed class TestDerivedAfterAttackAction : MonoBehaviour, IActionToolV2, IActionExecReportV2
    {
        static readonly WaitForSeconds WAIT = new(0.1f);
        static readonly string[] EMPTY_TAGS = Array.Empty<string>();

        bool _reportPending;
        int _usedSeconds;
        int _refundedSeconds;

        public string Id => "Derived_AfterAttack";
        public ActionKind Kind => ActionKind.Derived;
        public IReadOnlyCollection<string> ChainTags => EMPTY_TAGS;

        public bool CanChainAfter(string previousId, IReadOnlyCollection<string> previousTags)
            => string.Equals(previousId, "Attack", StringComparison.Ordinal);

        public ActionCostPlan PlannedCost(Hex hex)
            => new(true, 1, 0, primaryTimeSeconds: 1, detail: "(time=1s)");

        public void OnEnterAim() { }
        public void OnExitAim() { }
        public void OnHover(Hex hex) { }

        public IEnumerator OnConfirm(Hex hex)
        {
            _usedSeconds = 1;
            _refundedSeconds = 0;
            _reportPending = true;
            yield return WAIT;
            Debug.Log("[TestDerived] cast", this);
        }

        int IActionExecReportV2.UsedSeconds => _reportPending ? _usedSeconds : 0;
        int IActionExecReportV2.RefundedSeconds => _reportPending ? _refundedSeconds : 0;

        void IActionExecReportV2.Consume()
        {
            _reportPending = false;
            _usedSeconds = 0;
            _refundedSeconds = 0;
        }
    }
}