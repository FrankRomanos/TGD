using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;
using TGD.CombatV2.Targeting;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class MoveToolCompat : MonoBehaviour, IActionToolV2, IActionExecReportV2
    {
        static readonly string[] NO_TAGS = Array.Empty<string>();

        public HexClickMover mover;
        public TargetingSpec specEmptyOnly = new()
        {
            occupant = TargetOccupantMask.Empty,
            terrain = TargetTerrainMask.NonObstacle,
            allowSelf = false,
            requireEmpty = true,
            requireOccupied = false
        };

        ITargetValidator _validator;
        TurnManagerV2 _turnManager;

        void Awake()
        {
            if (!mover)
                mover = GetComponent<HexClickMover>();
            ConfigureLegacy();
        }

        void ConfigureLegacy()
        {
            if (!mover)
                return;
            mover.UseTurnManager = true;
            mover.ManageEnergyLocally = false;
            mover.ManageTurnTimeLocally = false;
        }

        public void SetValidator(ITargetValidator validator) => _validator = validator;

        public void AttachTurnManager(TurnManagerV2 tm)
        {
            _turnManager = tm;
            mover?.AttachTurnManager(tm);
        }

        public HexClickMover Legacy => mover;

        public string Id => mover != null ? mover.Id : "Move";

        public ActionKind Kind => ActionKind.Standard;

        public IReadOnlyCollection<string> ChainTags => NO_TAGS;

        public bool CanChainAfter(string previousId, IReadOnlyCollection<string> previousTags) => false;

        public ActionCostPlan PlannedCost(Hex hex)
        {
            var check = ValidateTarget(hex);
            if (!check.ok)
                return ActionCostPlan.Invalid($"(reason={check.reason})");

            int timeSeconds = Mathf.Max(1, Mathf.CeilToInt(mover != null && mover.config != null ? mover.config.timeCostSeconds : 1f));
            int energyPerSecond = Mathf.Max(0, mover != null && mover.config != null ? mover.config.energyCost : 0);
            int moveEnergy = energyPerSecond * timeSeconds;

            return new ActionCostPlan(true, timeSeconds, moveEnergy, timeSeconds, 0, moveEnergy, 0, "(plan=MoveOnly)");
        }

        public void OnEnterAim() => mover?.OnEnterAim();

        public void OnExitAim() => mover?.OnExitAim();

        public void OnHover(Hex hex) => mover?.OnHover(hex);

        public IEnumerator OnConfirm(Hex hex)
        {
            var check = ValidateTarget(hex);
            if (!check.ok)
            {
                LogPreDeductFail(check.reason);
                yield break;
            }

            if (mover == null)
                yield break;

            var routine = mover.OnConfirm(hex);
            if (routine == null)
                yield break;

            while (routine.MoveNext())
                yield return routine.Current;
        }

        public bool TryPrecheckAim(out string reason)
        {
            reason = null;
            if (mover == null || mover.driver == null || !mover.driver.IsReady)
            {
                reason = "(not-ready)";
                return false;
            }

            var unit = mover.driver.UnitRef;
            if (_turnManager != null && unit != null)
            {
                int needSec = Mathf.Max(1, Mathf.CeilToInt(mover.config != null ? mover.config.timeCostSeconds : 1f));
                var budget = _turnManager.GetBudget(unit);
                if (budget == null || !budget.HasTime(needSec))
                {
                    reason = "(no-time)";
                    return false;
                }

                var resources = _turnManager.GetResources(unit);
                if (resources != null)
                {
                    int energyPerSecond = Mathf.Max(0, mover.config != null ? mover.config.energyCost : 0);
                    int requiredEnergy = energyPerSecond * needSec;
                    if (requiredEnergy > 0 && resources.Get("Energy") < requiredEnergy)
                    {
                        reason = "(no-energy)";
                        return false;
                    }
                }
            }

            return true;
        }

        TargetCheckResult ValidateTarget(Hex hex)
        {
            if (mover == null || mover.driver == null)
                return new TargetCheckResult { ok = false, reason = TargetInvalidReason.Unknown };
            if (_validator == null)
                return new TargetCheckResult { ok = true, reason = TargetInvalidReason.None };
            return _validator.Check(mover.driver.UnitRef, hex, specEmptyOnly);
        }

        void LogPreDeductFail(TargetInvalidReason reason)
        {
            var unit = mover != null ? mover.driver?.UnitRef : null;
            ActionPhaseLogger.Log(unit, Id, ActionPhase.W2_PreDeductCheckFail, $"(reason={reason})");
        }

        int IActionExecReportV2.UsedSeconds
            => mover is IActionExecReportV2 exec ? exec.UsedSeconds : 0;

        int IActionExecReportV2.RefundedSeconds
            => mover is IActionExecReportV2 exec ? exec.RefundedSeconds : 0;

        void IActionExecReportV2.Consume()
        {
            if (mover is IActionExecReportV2 exec)
                exec.Consume();
        }
    }
}
