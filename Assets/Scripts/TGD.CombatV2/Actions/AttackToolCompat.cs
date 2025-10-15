using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;
using TGD.CombatV2.Targeting;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class AttackToolCompat : MonoBehaviour, IActionToolV2, IActionExecReportV2, IActionEnergyReportV2
    {
        static readonly string[] NO_TAGS = Array.Empty<string>();

        public AttackControllerV2 attack;
        public TargetingSpec specEnemyOnly = new()
        {
            occupant = TargetOccupantMask.Enemy,
            terrain = TargetTerrainMask.NonObstacle,
            allowSelf = false,
            requireEmpty = false,
            requireOccupied = true
        };

        public TargetingSpec specGround = new()
        {
            occupant = TargetOccupantMask.Empty,
            terrain = TargetTerrainMask.NonObstacle,
            allowSelf = false,
            requireEmpty = true,
            requireOccupied = false
        };

        enum AttackPlanKind
        {
            Invalid,
            MoveOnly,
            MoveAndAttack
        }

        ITargetValidator _validator;
        TurnManagerV2 _turnManager;

        void Awake()
        {
            if (!attack)
                attack = GetComponent<AttackControllerV2>();
            ConfigureLegacy();
        }

        void ConfigureLegacy()
        {
            if (!attack)
                return;
            attack.UseTurnManager = true;
            attack.ManageEnergyLocally = false;
            attack.ManageTurnTimeLocally = false;
        }

        public void SetValidator(ITargetValidator validator) => _validator = validator;

        public void AttachTurnManager(TurnManagerV2 tm)
        {
            _turnManager = tm;
            attack?.AttachTurnManager(tm);
        }

        public AttackControllerV2 Legacy => attack;

        public string Id => attack != null ? attack.Id : "Attack";

        public ActionKind Kind => ActionKind.Standard;

        public IReadOnlyCollection<string> ChainTags => NO_TAGS;

        public bool CanChainAfter(string previousId, IReadOnlyCollection<string> previousTags) => false;

        public ActionCostPlan PlannedCost(Hex hex)
        {
            var check = ValidateTarget(hex, out var planKind);
            if (!check.ok)
                return ActionCostPlan.Invalid($"(reason={check.reason})");

            int moveSeconds = Mathf.Max(1, Mathf.CeilToInt(attack != null && attack.moveConfig != null ? attack.moveConfig.timeCostSeconds : 1f));
            int moveEnergyRate = Mathf.Max(0, attack != null && attack.moveConfig != null ? attack.moveConfig.energyCost : 0);
            int moveEnergy = moveEnergyRate * moveSeconds;
            int attackSeconds = Mathf.Max(0, attack != null && attack.attackConfig != null ? attack.attackConfig.baseTimeSeconds : 0);
            int attackEnergy = Mathf.Max(0, attack != null && attack.attackConfig != null ? attack.attackConfig.baseEnergyCost : 0);

            if (planKind == AttackPlanKind.MoveOnly)
            {
                return new ActionCostPlan(true, moveSeconds, moveEnergy, moveSeconds, 0, moveEnergy, 0, "(plan=MoveOnly)");
            }

            int totalSeconds = moveSeconds + attackSeconds;
            int totalEnergy = moveEnergy + attackEnergy;
            return new ActionCostPlan(true, totalSeconds, totalEnergy, moveSeconds, attackSeconds, moveEnergy, attackEnergy, "(plan=Move+Attack)");
        }

        public void OnEnterAim() => attack?.OnEnterAim();

        public void OnExitAim() => attack?.OnExitAim();

        public void OnHover(Hex hex) => attack?.OnHover(hex);

        public IEnumerator OnConfirm(Hex hex)
        {
            var check = ValidateTarget(hex, out var planKind);
            if (!check.ok)
            {
                LogPreDeductFail(check.reason);
                yield break;
            }

            if (attack == null)
                yield break;

            var routine = attack.OnConfirm(hex);
            if (routine == null)
                yield break;

            while (routine.MoveNext())
                yield return routine.Current;
        }

        public bool TryPrecheckAim(out string reason)
        {
            reason = null;
            if (attack == null || attack.driver == null || !attack.driver.IsReady)
            {
                reason = "(not-ready)";
                return false;
            }

            var unit = attack.driver.UnitRef;
            if (_turnManager != null && unit != null)
            {
                int needSec = Mathf.Max(1, Mathf.CeilToInt(attack.moveConfig != null ? attack.moveConfig.timeCostSeconds : 1f));
                var budget = _turnManager.GetBudget(unit);
                if (budget == null || !budget.HasTime(needSec))
                {
                    reason = "(no-time)";
                    return false;
                }
            }

            if (_turnManager != null && unit != null)
            {
                var resources = _turnManager.GetResources(unit);
                if (resources != null)
                {
                    int moveRate = Mathf.Max(0, attack.moveConfig != null ? attack.moveConfig.energyCost : 0);
                    int needEnergy = moveRate * Mathf.Max(1, Mathf.CeilToInt(attack.moveConfig != null ? attack.moveConfig.timeCostSeconds : 1f));
                    bool anyCost = moveRate > 0 || (attack.attackConfig != null && attack.attackConfig.baseEnergyCost > 0);
                    int current = resources.Get("Energy");
                    if (anyCost && current <= 0)
                    {
                        reason = "(no-energy)";
                        return false;
                    }
                    if (moveRate > 0 && current < moveRate)
                    {
                        reason = "(no-energy)";
                        return false;
                    }
                    if (needEnergy > 0 && current < needEnergy)
                    {
                        reason = "(no-energy)";
                        return false;
                    }
                }
            }

            return true;
        }

        TargetCheckResult ValidateTarget(Hex hex, out AttackPlanKind planKind)
        {
            planKind = AttackPlanKind.Invalid;
            if (attack == null || attack.driver == null)
                return new TargetCheckResult { ok = false, reason = TargetInvalidReason.Unknown };
            if (_validator == null)
            {
                planKind = AttackPlanKind.MoveAndAttack;
                return new TargetCheckResult { ok = true, reason = TargetInvalidReason.None };
            }

            var actor = attack.driver.UnitRef;
            var enemyCheck = _validator.Check(actor, hex, specEnemyOnly);
            if (enemyCheck.ok)
            {
                planKind = AttackPlanKind.MoveAndAttack;
                return enemyCheck;
            }

            var groundCheck = _validator.Check(actor, hex, specGround);
            if (groundCheck.ok)
            {
                planKind = AttackPlanKind.MoveOnly;
                return groundCheck;
            }

            var reason = enemyCheck.reason != TargetInvalidReason.None ? enemyCheck.reason : groundCheck.reason;
            var hit = enemyCheck.hitUnit != null ? enemyCheck.hitUnit : groundCheck.hitUnit;
            return new TargetCheckResult { ok = false, reason = reason, hitUnit = hit };
        }

        void LogPreDeductFail(TargetInvalidReason reason)
        {
            var unit = attack != null ? attack.driver?.UnitRef : null;
            ActionPhaseLogger.Log(unit, Id, ActionPhase.W2_PreDeductCheckFail, $"(reason={reason})");
        }

        int IActionExecReportV2.UsedSeconds
            => attack is IActionExecReportV2 exec ? exec.UsedSeconds : 0;

        int IActionExecReportV2.RefundedSeconds
            => attack is IActionExecReportV2 exec ? exec.RefundedSeconds : 0;

        void IActionExecReportV2.Consume()
        {
            if (attack is IActionExecReportV2 exec)
                exec.Consume();
        }

        int IActionEnergyReportV2.ReportMoveEnergyNet
            => attack is IActionEnergyReportV2 energy ? energy.ReportMoveEnergyNet : 0;

        int IActionEnergyReportV2.ReportAttackEnergyNet
            => attack is IActionEnergyReportV2 energy ? energy.ReportAttackEnergyNet : 0;
    }
}
