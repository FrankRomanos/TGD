using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public sealed class TestFreePingAction : MonoBehaviour, IActionToolV2, IActionExecReportV2
    {
        static readonly WaitForSeconds WAIT = new(0.1f);
        static readonly string[] EMPTY_TAGS = Array.Empty<string>();

        bool _reportPending;
        int _usedSeconds;
        int _refundedSeconds;

        [Header("Test Cost")]
        public int energyCost = 5;

        public string Id => "Free_Ping";
        public ActionKind Kind => ActionKind.Free;
        public IReadOnlyCollection<string> ChainTags => EMPTY_TAGS;

        public bool CanChainAfter(string previousId, IReadOnlyCollection<string> previousTags) => false;

        public ActionCostPlan PlannedCost(Hex hex)
        {
            int energy = Mathf.Max(0, energyCost);
            return new ActionCostPlan(true, 0, energy, detail: $"(energy={energy})");
        }

        public void OnEnterAim() { }
        public void OnExitAim() { }
        public void OnHover(Hex hex) { }

        public IEnumerator OnConfirm(Hex hex)
        {
            _usedSeconds = 0;
            _refundedSeconds = 0;
            _reportPending = true;
            yield return WAIT;
            Debug.Log("[TestFree] cast", this);
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