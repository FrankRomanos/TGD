using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public enum SettlementMode
    {
        Local,
        TMV2,
    }

    public sealed class CombatActionManagerV2 : MonoBehaviour
    {
        public SettlementMode settlement = SettlementMode.TMV2;
        public TurnManagerV2 tm;
        public EnergyService energy;
        public bool debugLog = true;

        public void AimBegin(Unit u, ActionKindV2 kind)
        {
            Log($"[Action] {u?.Id} [{kind}] W1_AimBegin");
        }

        public void AimCancel(Unit u, ActionKindV2 kind)
        {
            Log($"[Action] {u?.Id} [{kind}] W1_AimCancel");
        }

        public bool Confirm(Unit u, ActionPlanV2 plan, out string failReason)
        {
            Log($"[Action] {u?.Id} [{plan.kind}] W2_ConfirmStart");

            Log($"[Action] {u?.Id} [{plan.kind}] W2_PrecheckOk");

            int remain = tm != null ? tm.RemainingSeconds(u) : 0;
            int energyBefore = energy != null ? energy.Current(u) : 0;
            int tPlan = plan.planSecsMove + plan.planSecsAtk;
            int ePlan = plan.planEnergyMove + plan.planEnergyAtk;

            Log($"[Gate] W2 PreDeduct (move={plan.planSecsMove}s/{plan.planEnergyMove}, atk={plan.planSecsAtk}s/{plan.planEnergyAtk}, total={tPlan}s/{ePlan}, remain={remain}s, energy={energyBefore})");

            if (tm != null && remain < tPlan)
            {
                failReason = "lackTime";
                Log($"[Action] {u?.Id} [{plan.kind}] W2_PreDeductCheckFail (reason=lackTime)");
                Log($"[Action] {u?.Id} [{plan.kind}] W2_ConfirmAbort (reason=lackTime)");
                return false;
            }

            if (energy != null && !energy.CanAfford(u, ePlan))
            {
                failReason = "lackEnergy";
                Log($"[Action] {u?.Id} [{plan.kind}] W2_PreDeductCheckFail (reason=lackEnergy)");
                Log($"[Action] {u?.Id} [{plan.kind}] W2_ConfirmAbort (reason=lackEnergy)");
                return false;
            }

            failReason = null;
            return true;
        }

        public ActionExecReportV2 Execute(Unit u, ActionPlanV2 plan, ICombatActionToolV2 tool)
        {
            int budgetBefore = tm != null ? tm.RemainingSeconds(u) : 0;
            int energyBefore = energy != null ? energy.Current(u) : 0;
            Log($"[Action] {u?.Id} [{plan.kind}] W3_ExecuteBegin (budgetBefore={budgetBefore}, energyBefore={energyBefore})");

            ActionExecReportV2 report = tool != null ? tool.Execute(plan) : default;

            if (plan.kind == ActionKindV2.Move)
            {
                Log($"[Move] Use secs={report.usedSecsMove}s refund={report.refundedSecs}s energy={report.energyMoveNet} U={u?.Id}{FlagTail(report)}");
            }
            else
            {
                Log($"[Attack] Use moveSecs={report.usedSecsMove}s atkSecs={report.usedSecsAtk}s energyMove={report.energyMoveNet} energyAtk={report.energyAtkNet} U={u?.Id}{FlagTail(report)}");
            }

            Log($"[Action] {u?.Id} [{plan.kind}] W3_ExecuteEnd");
            return report;

            string FlagTail(ActionExecReportV2 r)
            {
                return (r.flags & ActionExecFlagsV2.FreeMoveApplied) != 0 ? " (FreeMove)" : string.Empty;
            }
        }

        public void Resolve(Unit u, ActionPlanV2 plan, ActionExecReportV2 r)
        {
            float used = r.usedSecsMove + r.usedSecsAtk;
            float refunded = r.refundedSecs + FreeMoveRefundSeconds(r);
            float net = Mathf.Max(0f, used - refunded);

            Log($"[Action] {u?.Id} [{plan.kind}] W4_ResolveBegin (used={used}, refunded={refunded}, net={net}, energyMove={r.energyMoveNet}, energyAtk={r.energyAtkNet})");

            if (tm != null)
            {
                if (net > 0f)
                {
                    tm.SpendSeconds(u, Mathf.CeilToInt(net), suppressLog: true);
                }

                if (refunded > 0f)
                {
                    tm.RefundSeconds(u, Mathf.CeilToInt(refunded), FreeMoveReason(r), suppressLog: true);
                }
            }

            if (energy != null)
            {
                if (r.energyMoveNet != 0)
                {
                    energy.Apply(u, -r.energyMoveNet, plan.kind.ToString());
                }

                if (r.energyAtkNet != 0)
                {
                    energy.Apply(u, -r.energyAtkNet, plan.kind.ToString());
                }

                if ((r.flags & ActionExecFlagsV2.FreeMoveApplied) != 0)
                {
                    int energyPerSec = energy.MoveEnergyPerSecond(u);
                    if (energyPerSec != 0)
                    {
                        energy.Apply(u, energyPerSec, "FreeMove");
                    }
                }
            }

            LogTimeAndRes(u, r, net, refunded);
            int afterBudget = tm != null ? tm.RemainingSeconds(u) : 0;
            int afterEnergy = energy != null ? energy.Current(u) : 0;
            Log($"[Action] {u?.Id} [{plan.kind}] W4_ResolveEnd (budgetAfter={afterBudget}, energyAfter={afterEnergy})");
        }

        int FreeMoveRefundSeconds(ActionExecReportV2 x)
        {
            return (x.flags & ActionExecFlagsV2.FreeMoveApplied) != 0 ? 1 : 0;
        }

        string FreeMoveReason(ActionExecReportV2 x)
        {
            return (x.flags & ActionExecFlagsV2.FreeMoveApplied) != 0 ? "FreeMove" : "ToolReported";
        }

        void LogTimeAndRes(Unit unit, ActionExecReportV2 rep, float netSecs, float refundedSecs)
        {
            if (tm != null)
            {
                int afterSecs = tm.RemainingSeconds(unit);
                if (netSecs > 0f)
                {
                    Log($"[Time] Spend {unit?.Id} {Mathf.CeilToInt(netSecs)}s -> Remain={afterSecs}");
                }

                if (refundedSecs > 0f)
                {
                    Log($"[Time] Refund {unit?.Id} {Mathf.CeilToInt(refundedSecs)}s (reason={FreeMoveReason(rep)}) -> Remain={afterSecs}");
                }
            }

            if (energy != null)
            {
                int afterEnergy = energy.Current(unit);
                if (rep.energyMoveNet > 0)
                {
                    Log($"[Res] Spend {unit?.Id}:Energy -{rep.energyMoveNet} -> {afterEnergy} (Move)");
                }
                else if (rep.energyMoveNet < 0)
                {
                    Log($"[Res] Refund {unit?.Id}:Energy +{-rep.energyMoveNet} -> {afterEnergy} (Move_Adjust)");
                }

                if (rep.energyAtkNet > 0)
                {
                    Log($"[Res] Spend {unit?.Id}:Energy -{rep.energyAtkNet} -> {afterEnergy} (Attack)");
                }
                else if (rep.energyAtkNet < 0)
                {
                    Log($"[Res] Refund {unit?.Id}:Energy +{-rep.energyAtkNet} -> {afterEnergy} (Attack_Adjust)");
                }

                if ((rep.flags & ActionExecFlagsV2.FreeMoveApplied) != 0)
                {
                    int energyPerSec = energy.MoveEnergyPerSecond(unit);
                    if (energyPerSec != 0)
                    {
                        Log($"[Res] Refund {unit?.Id}:Energy +{energyPerSec} -> {energy.Current(unit)} (FreeMove)");
                    }
                }
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        void Log(string message)
        {
            if (debugLog)
            {
                Debug.Log(message, this);
            }
        }
    }
}
