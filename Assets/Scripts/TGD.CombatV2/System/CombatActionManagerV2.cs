using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class CombatActionManagerV2 : MonoBehaviour
    {
        [Header("Refs")]
        public HexBoardAuthoringLite authoring;
        public Camera pickCamera;
        public LayerMask pickMask = ~0;
        public float pickPlaneY = 0.01f;
        public float rayMaxDistance = 2000f;

        [Header("Turn Runtime")]
        public TurnManagerV2 turnManager;
        public HexBoardTestDriver unitDriver;

        [Header("Tools (drag any components that implement IActionToolV2)")]
        public List<MonoBehaviour> tools = new();

        [Header("Keybinds")]
        public KeyCode keyMoveAim = KeyCode.V;
        public KeyCode keyAttackAim = KeyCode.A;

        public bool debugLog = true;
        public bool quietInternalToolLogs = true;

        enum Phase
        {
            Idle,
            Aiming,
            Executing
        }

        [SerializeField]
        Phase _phase = Phase.Idle;
        public bool IsExecuting => _phase == Phase.Executing;

        readonly Dictionary<string, List<IActionToolV2>> _toolsById = new();
        IActionToolV2 _activeTool;
        Unit _currentUnit;
        Hex? _hover;

        struct PlannedCost
        {
            public int moveSecs;
            public int atkSecs;
            public int moveEnergy;
            public int atkEnergy;
            public bool valid;
            public bool isEnemyTarget;
            public bool isFreeMoveCandidate;
            public string failReason;

            public int TotalSeconds => Mathf.Max(0, moveSecs + atkSecs);
            public int TotalEnergy => Mathf.Max(0, moveEnergy + atkEnergy);
        }

        struct PreDeduct
        {
            public bool valid;
            public int planMoveSecs;
            public int planAtkSecs;
            public int planEnergyMove;
            public int planEnergyAtk;
            public bool isEnemyTarget;
            public bool isFreeMoveCandidate;
            public int budgetBefore;
            public int budgetAfter;
            public int energyBefore;
            public int energyAfter;

            public int PlanTotalSeconds => Mathf.Max(0, planMoveSecs + planAtkSecs);
        }

        struct ActionPlan
        {
            public string kind;
            public Hex target;
            public PlannedCost cost;
        }

        struct ExecReportData
        {
            public bool valid;
            public int usedSecsMove;
            public int usedSecsAtk;
            public int refundedSecs;
            public int refundedMoveSecs;
            public int refundedAtkSecs;
            public bool freeMoveApplied;
            public bool rolledBack;
        }

        PreDeduct _plan;

        void Awake()
        {
            foreach (var mb in tools)
            {
                if (!mb) continue;
                WireTurnManager(mb);
                if (mb is IActionToolV2 tool)
                {
                    if (!_toolsById.TryGetValue(tool.Id, out var list))
                    {
                        list = new List<IActionToolV2>();
                        _toolsById[tool.Id] = list;
                    }
                    if (!list.Contains(tool))
                        list.Add(tool);
                }
            }
        }

        void WireTurnManager(MonoBehaviour mb)
        {
            if (mb == null) return;
            switch (mb)
            {
                case AttackControllerV2 attack:
                    attack.AttachTurnManager(turnManager);
                    attack.suppressInternalLogs = quietInternalToolLogs;
                    break;
                case HexClickMover mover:
                    mover.AttachTurnManager(turnManager);
                    mover.suppressInternalLogs = quietInternalToolLogs;
                    if (turnManager != null)
                        WireMoveCostAdapter(mover.costProvider as MoveCostServiceV2Adapter);
                    break;
                case MoveCostServiceV2Adapter moveAdapter:
                    WireMoveCostAdapter(moveAdapter);
                    break;
                case AttackCostServiceV2Adapter attackAdapter:
                    attackAdapter.turnManager = turnManager;
                    break;
            }
        }

        void WireMoveCostAdapter(MoveCostServiceV2Adapter adapter)
        {
            if (adapter == null) return;
            adapter.turnManager = turnManager;
        }

        void OnEnable()
        {
            if (turnManager != null) turnManager.TurnStarted += OnTurnStarted;
        }

        void OnDisable()
        {
            if (turnManager != null) turnManager.TurnStarted -= OnTurnStarted;
        }

        void OnTurnStarted(Unit unit)
        {
            _currentUnit = unit;
            if (_activeTool != null && ResolveUnit(_activeTool) != _currentUnit)
                Cancel(false);
        }

        IActionToolV2 SelectTool(string id)
        {
            if (!_toolsById.TryGetValue(id, out var list)) return null;
            foreach (var tool in list)
            {
                if (ResolveUnit(tool) == _currentUnit)
                    return tool;
            }
            return null;
        }

        void Update()
        {
            if (_phase == Phase.Idle)
            {
                if (Input.GetKeyDown(keyMoveAim)) RequestAim("Move");
                if (Input.GetKeyDown(keyAttackAim)) RequestAim("Attack");
            }

            if (_phase == Phase.Aiming)
            {
                var h = PickHexUnderMouse();
                if (h.HasValue && (!_hover.HasValue || !_hover.Value.Equals(h.Value)))
                {
                    _hover = h;
                    _activeTool?.OnHover(h.Value);
                }

                if (Input.GetMouseButtonDown(0)) Confirm();
                if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) Cancel(true);
            }
        }

        Unit ResolveUnit(IActionToolV2 tool)
        {
            if (tool is HexClickMover mover && mover != null && mover.driver != null)
                return mover.driver.UnitRef;
            if (tool is AttackControllerV2 attack && attack != null && attack.driver != null)
                return attack.driver.UnitRef;
            return unitDriver != null ? unitDriver.UnitRef : null;
        }

        public void RequestAim(string toolId)
        {
            if (_phase != Phase.Idle) return;

            var tool = SelectTool(toolId);
            if (tool == null) return;
            if (IsExecuting || IsAnyToolBusy()) return;

            var unit = ResolveUnit(tool);
            if (_currentUnit != null && unit != _currentUnit)
                return;

            if (!TryBeginAim(tool, unit, out var reason))
            {
                if (!string.IsNullOrEmpty(reason))
                    ActionPhaseLogger.Log(unit, tool.Id, "W1_AimReject", $"(reason={reason})");
                return;
            }

            if (_activeTool != null)
                CleanupAfterAbort(_activeTool, false);

            _activeTool = tool;
            _hover = null;
            _activeTool.OnEnterAim();
            _phase = Phase.Aiming;
            ActionPhaseLogger.Log(unit, tool.Id, "W1_AimBegin");
        }

        public void Cancel(bool userInitiated = false)
        {
            if (_phase != Phase.Aiming || _activeTool == null)
                return;

            CleanupAfterAbort(_activeTool, userInitiated);
        }

        public void Confirm()
        {
            if (_phase != Phase.Aiming || _activeTool == null)
                return;

            var unit = ResolveUnit(_activeTool);
            var hex = _hover ?? PickHexUnderMouse();

            StartCoroutine(ConfirmRoutine(_activeTool, unit, hex));
        }

        void TryHideAllAimUI()
        {
            if (_activeTool != null)
                _activeTool.OnExitAim();

            foreach (var mb in tools)
            {
                if (mb == null || ReferenceEquals(mb, _activeTool))
                    continue;

                switch (mb)
                {
                    case HexClickMover mover:
                        mover.HideRange();
                        break;
                    case AttackControllerV2 attack:
                        attack.OnExitAim();
                        break;
                }
            }
        }

        IEnumerator ConfirmRoutine(IActionToolV2 tool, Unit unit, Hex? target)
        {
            _plan = default;
            string kind = tool.Id;
            ActionPhaseLogger.Log(unit, kind, "W2_ConfirmStart");

            if (!target.HasValue)
            {
                ActionPhaseLogger.Log(unit, kind, "W2_PrecheckOk");
                ActionPhaseLogger.Log(unit, kind, "W2_PreDeductCheckFail", "(reason=targetInvalid)");
                ActionPhaseLogger.Log(unit, kind, "W2_ConfirmAbort", "(reason=targetInvalid)");
                NotifyAttackRejected(tool, unit, FormatAttackReject(AttackRejectReasonV2.NoPath, "Invalid target."));
                CleanupAfterAbort(tool, false);
                yield break;
            }

            string rejectToken = null;
            if (!TryBeginAim(tool, unit, out var aimRaw, false))
            {
                ActionPhaseLogger.Log(unit, kind, "W2_PrecheckOk");
                ActionPhaseLogger.Log(unit, kind, "W2_PreDeductCheckFail", "(reason=notReady)");
                ActionPhaseLogger.Log(unit, kind, "W2_ConfirmAbort", "(reason=notReady)");
                string aimReason = MapAimReason(aimRaw);
                rejectToken = aimReason switch
                {
                    "lackTime" => FormatAttackReject(AttackRejectReasonV2.CantMove, "No more time."),
                    "lackEnergy" => FormatAttackReject(AttackRejectReasonV2.NotEnoughResource, "Not enough energy."),
                    "cooldown" => FormatAttackReject(AttackRejectReasonV2.OnCooldown, "Attack is on cooldown."),
                    _ => FormatAttackReject(AttackRejectReasonV2.NotReady, "Attack not ready.")
                };
                NotifyAttackRejected(tool, unit, rejectToken);
                CleanupAfterAbort(tool, false);
                yield break;
            }

            ActionPhaseLogger.Log(unit, kind, "W2_PrecheckOk");

            var actionPlan = new ActionPlan
            {
                kind = kind,
                target = target.Value,
                cost = BuildPlannedCost(tool, target.Value)
            };

            var cost = actionPlan.cost;
            var budget = turnManager != null && unit != null ? turnManager.GetBudget(unit) : null;
            var resources = turnManager != null && unit != null ? turnManager.GetResources(unit) : null;
            var cooldowns = turnManager != null && unit != null ? turnManager.GetCooldowns(unit) : null;

            int planMoveSecs = Mathf.Max(0, cost.moveSecs);
            int planAtkSecs = Mathf.Max(0, cost.atkSecs);
            int planTotalSecs = Mathf.Max(0, cost.TotalSeconds);
            int planEnergyMove = Mathf.Max(0, cost.moveEnergy);
            int planEnergyAtk = Mathf.Max(0, cost.atkEnergy);

            int budgetBefore = budget != null ? budget.Remaining : 0;
            int energyBefore = resources != null ? resources.Get("Energy") : 0;

            string failReason = null;
            rejectToken = null;
            if (!cost.valid)
            {
                failReason = "targetInvalid";
                rejectToken = !string.IsNullOrEmpty(cost.failReason)
                    ? cost.failReason
                    : FormatAttackReject(AttackRejectReasonV2.NoPath, "Invalid target.");
            }
            else if (budget != null && planTotalSecs > 0 && !budget.HasTime(planTotalSecs))
            {
                failReason = "lackTime";
                rejectToken = FormatAttackReject(AttackRejectReasonV2.CantMove, "No more time.");
            }
            else if (resources != null && (planEnergyMove + planEnergyAtk) > 0 && !resources.Has("Energy", planEnergyMove + planEnergyAtk))
            {
                failReason = "lackEnergy";
                rejectToken = FormatAttackReject(AttackRejectReasonV2.NotEnoughResource, "Not enough energy.");
            }
            else if (!IsCooldownReadyForConfirm(tool, cooldowns))
            {
                failReason = "cooldown";
                rejectToken = FormatAttackReject(AttackRejectReasonV2.OnCooldown, "Attack is on cooldown.");
            }

            if (failReason != null)
            {
                ActionPhaseLogger.Log(unit, kind, "W2_PreDeductCheckFail", $"(reason={failReason})");
                ActionPhaseLogger.Log(unit, kind, "W2_ConfirmAbort", $"(reason={failReason})");
                if (!string.IsNullOrEmpty(rejectToken))
                    NotifyAttackRejected(tool, unit, rejectToken);
                CleanupAfterAbort(tool, false);
                yield break;
            }

            ActionPhaseLogger.Log(unit, kind, "W2_PreDeductCheckOk");

            if (budget != null)
            {
                if (planMoveSecs > 0)
                {
                    budget.SpendTime(planMoveSecs);
                    Log($"[Time] Spend {planMoveSecs}s (PreDeduct_Move) -> Remain={budget.Remaining}");
                }

                if (planAtkSecs > 0)
                {
                    budget.SpendTime(planAtkSecs);
                    Log($"[Time] Spend {planAtkSecs}s (PreDeduct_Attack) -> Remain={budget.Remaining}");
                }
            }

            if (resources != null)
            {
                if (planEnergyMove > 0)
                {
                    resources.Spend("Energy", planEnergyMove, "PreDeduct_Move");
                    Log($"[Res] Spend Energy -{planEnergyMove} (PreDeduct_Move) -> {resources.Get("Energy")}");
                }

                if (planEnergyAtk > 0)
                {
                    resources.Spend("Energy", planEnergyAtk, "PreDeduct_Attack");
                    Log($"[Res] Spend Energy -{planEnergyAtk} (PreDeduct_Attack) -> {resources.Get("Energy")}");
                }
            }

            int budgetAfter = budget != null ? budget.Remaining : budgetBefore;
            int energyAfter = resources != null ? resources.Get("Energy") : energyBefore;

            Log($"[Gate] W2 PreDeduct (move={planMoveSecs}s/atk={planAtkSecs}s, total={planTotalSecs}s, energyMove={planEnergyMove}, energyAtk={planEnergyAtk}, remain={budgetBefore}->{budgetAfter}, energy={energyBefore}->{energyAfter})");

            _plan = new PreDeduct
            {
                valid = true,
                planMoveSecs = planMoveSecs,
                planAtkSecs = planAtkSecs,
                planEnergyMove = planEnergyMove,
                planEnergyAtk = planEnergyAtk,
                isEnemyTarget = cost.isEnemyTarget,
                isFreeMoveCandidate = cost.isFreeMoveCandidate,
                budgetBefore = budgetBefore,
                budgetAfter = budgetAfter,
                energyBefore = energyBefore,
                energyAfter = energyAfter
            };

            ActionPhaseLogger.Log(unit, kind, "W2_ChainPromptOpen", "(count=0)");
            ActionPhaseLogger.Log(unit, kind, "W2_ChainPromptAbort", "(auto-skip)");

            yield return ExecuteAndResolve(tool, unit, actionPlan, budget, resources);
        }

        IEnumerator ExecuteAndResolve(IActionToolV2 tool, Unit unit, ActionPlan plan, ITurnBudget budget, IResourcePool resources)
        {
            _phase = Phase.Executing;
            _hover = null;
            TryHideAllAimUI();

            int budgetBefore = _plan.valid ? _plan.budgetBefore : (budget != null ? budget.Remaining : 0);
            int energyBefore = _plan.valid ? _plan.energyBefore : (resources != null ? resources.Get("Energy") : 0);
            ActionPhaseLogger.Log(unit, plan.kind, "W3_ExecuteBegin", $"(budgetBefore={budgetBefore}, energyBefore={energyBefore})");

            var routine = tool.OnConfirm(plan.target);
            if (routine != null)
                yield return StartCoroutine(routine);

            var report = BuildExecReport(tool, out var exec);
            if (!report.valid || exec == null)
            {
                ActionPhaseLogger.Log(unit, plan.kind, "W3_ExecuteEnd");
                CleanupAfterAbort(tool, false);
                yield break;
            }

            ActionPhaseLogger.Log(unit, plan.kind, "W3_ExecuteEnd");

            Resolve(unit, plan, exec, report, budget, resources);
        }

        void Resolve(Unit unit, ActionPlan plan, IActionExecReportV2 exec, ExecReportData report, ITurnBudget budget, IResourcePool resources)
        {
            int usedMove = Mathf.Max(0, report.usedSecsMove);
            int usedAtk = Mathf.Max(0, report.usedSecsAtk);
            int used = Mathf.Max(0, usedMove + usedAtk);

            int planMoveSecs = _plan.valid ? Mathf.Max(0, _plan.planMoveSecs) : 0;
            int planAtkSecs = _plan.valid ? Mathf.Max(0, _plan.planAtkSecs) : 0;
            int planEnergyMove = _plan.valid ? Mathf.Max(0, _plan.planEnergyMove) : 0;
            int planEnergyAtk = _plan.valid ? Mathf.Max(0, _plan.planEnergyAtk) : 0;

            int speedRefundSecs = _plan.valid ? Mathf.Max(0, planMoveSecs - usedMove) : 0;
            int attackRollbackSecs = (_plan.valid && report.rolledBack) ? planAtkSecs : 0;
            int freeMoveRefundSecs = (_plan.valid && report.freeMoveApplied) ? 1 : 0;
            int refunded = Mathf.Max(0, speedRefundSecs + attackRollbackSecs + freeMoveRefundSecs);
            int net = used - refunded;

            int moveEnergyRate = planMoveSecs > 0 ? Mathf.RoundToInt(planEnergyMove / (float)Mathf.Max(1, planMoveSecs)) : 0;
            int speedRefundEnergy = (speedRefundSecs > 0 && moveEnergyRate > 0) ? speedRefundSecs * moveEnergyRate : 0;
            int freeMoveRefundEnergy = (_plan.valid && report.freeMoveApplied && moveEnergyRate > 0) ? moveEnergyRate : 0;
            if (!_plan.isEnemyTarget)
                freeMoveRefundEnergy = 0;
            int attackRefundEnergy = report.rolledBack ? planEnergyAtk : 0;

            int plannedTotal = _plan.valid ? Mathf.Max(0, _plan.PlanTotalSeconds) : 0;
            int extraSpendSecs = used > plannedTotal ? used - plannedTotal : 0;
            int extraMoveSecs = usedMove > planMoveSecs ? usedMove - planMoveSecs : 0;
            int extraSpendEnergyMove = (extraMoveSecs > 0 && moveEnergyRate > 0) ? extraMoveSecs * moveEnergyRate : 0;

            int totalMoveSpend = planEnergyMove + extraSpendEnergyMove;
            int totalMoveRefund = Mathf.Min(totalMoveSpend, speedRefundEnergy + freeMoveRefundEnergy);
            int energyMoveNet = totalMoveSpend - totalMoveRefund;

            int energyAtkNet = planEnergyAtk;
            if (report.rolledBack)
                energyAtkNet = Mathf.Max(0, energyAtkNet - Mathf.Min(energyAtkNet, attackRefundEnergy));

            string freeMoveTag = report.freeMoveApplied ? " freeMove=1" : string.Empty;
            ActionPhaseLogger.Log(unit, plan.kind, "W4_ResolveBegin", $"(used={used}, refunded={Mathf.Max(0, refunded)}, net={net}, energyMove={energyMoveNet}, energyAtk={energyAtkNet}{freeMoveTag})");

            if (budget != null)
            {
                if (extraSpendSecs > 0)
                {
                    budget.SpendTime(extraSpendSecs);
                    Log($"[Time] Spend {extraSpendSecs}s (Resolve_Extra) -> Remain={budget.Remaining}");
                }

                if (speedRefundSecs > 0)
                {
                    budget.RefundTime(speedRefundSecs);
                    Log($"[Time] Refund {speedRefundSecs}s (reason=Speed_Adjust) -> Remain={budget.Remaining}");
                }

                if (attackRollbackSecs > 0)
                {
                    budget.RefundTime(attackRollbackSecs);
                    Log($"[Time] Refund {attackRollbackSecs}s (reason=Attack_Adjust) -> Remain={budget.Remaining}");
                }

                if (freeMoveRefundSecs > 0)
                {
                    budget.RefundTime(freeMoveRefundSecs);
                    Log($"[Time] Refund {freeMoveRefundSecs}s (reason=FreeMove) -> Remain={budget.Remaining}");
                }
            }

            if (resources != null)
            {
                if (extraSpendEnergyMove > 0)
                {
                    resources.Spend("Energy", extraSpendEnergyMove, "Resolve_MoveExtra");
                    Log($"[Res] Spend Energy -{extraSpendEnergyMove} (Resolve_MoveExtra) -> {resources.Get("Energy")}");
                }

                int remainingMoveRefundEnergy = totalMoveSpend;
                if (speedRefundEnergy > 0 && remainingMoveRefundEnergy > 0)
                {
                    int refund = Mathf.Min(speedRefundEnergy, remainingMoveRefundEnergy);
                    resources.Refund("Energy", refund, "Speed_Adjust");
                    remainingMoveRefundEnergy -= refund;
                    Log($"[Res] Refund Energy +{refund} (Speed_Adjust) -> {resources.Get("Energy")}");
                }

                if (freeMoveRefundEnergy > 0 && remainingMoveRefundEnergy > 0)
                {
                    int refund = Mathf.Min(freeMoveRefundEnergy, remainingMoveRefundEnergy);
                    resources.Refund("Energy", refund, "FreeMove");
                    remainingMoveRefundEnergy -= refund;
                    Log($"[Res] Refund Energy +{refund} (FreeMove) -> {resources.Get("Energy")}");
                }

                if (attackRefundEnergy > 0)
                {
                    resources.Refund("Energy", attackRefundEnergy, "Attack_Adjust");
                    Log($"[Res] Refund Energy +{attackRefundEnergy} (Attack_Adjust) -> {resources.Get("Energy")}");
                }
            }

            LogExecSummary(unit, plan.kind, usedMove, usedAtk, speedRefundSecs + freeMoveRefundSecs, attackRollbackSecs, energyMoveNet, energyAtkNet, report.freeMoveApplied);

            int budgetAfter = budget != null ? budget.Remaining : 0;
            int energyAfter = resources != null ? resources.Get("Energy") : 0;
            ActionPhaseLogger.Log(unit, plan.kind, "W4_ResolveEnd", $"(budgetAfter={budgetAfter}, energyAfter={energyAfter})");

            exec.Consume();
            _activeTool = null;
            _hover = null;
            _phase = Phase.Idle;
            _plan = default;
        }

        ExecReportData BuildExecReport(IActionToolV2 tool, out IActionExecReportV2 exec)
        {
            exec = tool as IActionExecReportV2;
            if (exec == null)
                return default;

            var data = new ExecReportData
            {
                valid = true,
                usedSecsMove = Mathf.Max(0, exec.UsedSeconds),
                usedSecsAtk = 0,
                refundedSecs = Mathf.Max(0, exec.RefundedSeconds),
                refundedMoveSecs = 0,
                refundedAtkSecs = 0,
                freeMoveApplied = false,
                rolledBack = false
            };

            if (tool is HexClickMover mover)
            {
                data.usedSecsMove = Mathf.Max(0, mover.ReportUsedSeconds);
                data.refundedMoveSecs = Mathf.Max(0, mover.ReportRefundedSeconds);
                data.refundedAtkSecs = 0;
                data.refundedSecs = data.refundedMoveSecs;
                data.freeMoveApplied = mover.ReportFreeMoveApplied;
            }
            else if (tool is AttackControllerV2 attack)
            {
                if (attack.TryGetAttackBreakdown(out var breakdown))
                {
                    data.usedSecsMove = Mathf.Max(0, breakdown.usedMoveSecs);
                    data.usedSecsAtk = Mathf.Max(0, breakdown.usedAtkSecs);
                    data.refundedMoveSecs = Mathf.Max(0, breakdown.refundedMoveSecs);
                    int plannedAtk = _plan.valid ? Mathf.Max(0, _plan.planAtkSecs) : 0;
                    data.refundedAtkSecs = breakdown.rolledBack ? plannedAtk : 0;
                    data.refundedSecs = Mathf.Max(0, data.refundedMoveSecs + data.refundedAtkSecs);
                    data.freeMoveApplied = breakdown.freeMoveApplied;
                    data.rolledBack = breakdown.rolledBack;
                }
                else
                {
                    data.usedSecsMove = Mathf.Max(0, attack.ReportMoveUsedSeconds);
                    data.usedSecsAtk = Mathf.Max(0, attack.ReportAttackUsedSeconds);
                    int refundedMove = Mathf.Max(0, attack.ReportMoveRefundSeconds);
                    int refundedAtk = Mathf.Max(0, attack.ReportAttackRefundSeconds);
                    data.refundedMoveSecs = refundedMove;
                    data.refundedAtkSecs = refundedAtk;
                    data.refundedSecs = Mathf.Max(0, refundedMove + refundedAtk);
                    data.freeMoveApplied = attack.ReportFreeMoveApplied;
                    data.rolledBack = refundedAtk > 0;
                }
            }

            return data;
        }

        void LogExecSummary(Unit unit, string kind, int usedMove, int usedAtk, int refundedMove, int refundedAtk, int energyMoveNet, int energyAtkNet, bool freeMoveApplied)
        {
            string label = TurnManagerV2.FormatUnitLabel(unit);
            string freeMove = freeMoveApplied ? " (FreeMove)" : string.Empty;
            if (string.Equals(kind, "Move", StringComparison.OrdinalIgnoreCase))
            {
                Log($"[Move] Use secs={usedMove}s refund={refundedMove}s energy={energyMoveNet} U={label}{freeMove}");
            }
            else if (string.Equals(kind, "Attack", StringComparison.OrdinalIgnoreCase))
            {
                Log($"[Attack] Use moveSecs={usedMove}s atkSecs={usedAtk}s refundMove={refundedMove}s refundAtk={refundedAtk}s energyMove={energyMoveNet} energyAtk={energyAtkNet} U={label}{freeMove}");
            }
            else
            {
                int totalUsed = usedMove + usedAtk;
                int totalRefund = refundedMove + refundedAtk;
                int totalEnergy = energyMoveNet + energyAtkNet;
                Log($"[Action] {label} [{kind}] ExecSummary used={totalUsed}s refund={totalRefund}s energy={totalEnergy}{freeMove}");
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        void Log(string message)
        {
            if (debugLog)
                Debug.Log(message, this);
        }

        void CleanupAfterAbort(IActionToolV2 tool, bool logCancel)
        {
            if (tool == null) return;

            if (logCancel && _phase == Phase.Aiming)
            {
                var unit = ResolveUnit(tool);
                ActionPhaseLogger.Log(unit, tool.Id, "W1_AimCancel");
            }

            TryHideAllAimUI();
            if (_activeTool == tool)
                _activeTool = null;
            _hover = null;
            _phase = Phase.Idle;
            _plan = default;
        }

        bool TryBeginAim(IActionToolV2 tool, Unit unit, out string reason, bool raiseHud = true)
        {
            reason = null;
            string raw = null;
            bool ready = tool switch
            {
                HexClickMover mover => mover.TryPrecheckAim(out raw, raiseHud),
                AttackControllerV2 attack => attack.TryPrecheckAim(out raw, raiseHud),
                _ => true
            };

            if (!ready)
            {
                reason = MapAimReason(raw);
                return false;
            }

            if (turnManager != null && unit != null)
            {
                var cost = GetBaselineCost(tool);
                var budget = turnManager.GetBudget(unit);
                if (budget != null && cost.TotalSeconds > 0 && !budget.HasTime(cost.TotalSeconds))
                {
                    reason = "lackTime";
                    return false;
                }

                var resources = turnManager.GetResources(unit);
                if (resources != null && cost.TotalEnergy > 0 && !resources.Has("Energy", cost.TotalEnergy))
                {
                    reason = "lackEnergy";
                    return false;
                }

                var cooldowns = turnManager.GetCooldowns(unit);
                if (!IsCooldownReadyForConfirm(tool, cooldowns))
                {
                    reason = "cooldown";
                    return false;
                }
            }

            return true;
        }

        PlannedCost GetBaselineCost(IActionToolV2 tool)
        {
            if (tool is HexClickMover mover)
            {
                int secs = Mathf.Max(1, Mathf.CeilToInt(mover.config ? mover.config.timeCostSeconds : 1f));
                int energyRate = mover.config ? Mathf.Max(0, mover.config.energyCost) : 0;
                return new PlannedCost
                {
                    moveSecs = Mathf.Max(0, secs),
                    moveEnergy = Mathf.Max(0, energyRate),
                    atkSecs = 0,
                    atkEnergy = 0,
                    valid = true
                };
            }

            if (tool is AttackControllerV2 attack)
            {
                int moveSecs = 1;
                int moveEnergyRate = attack.moveConfig ? Mathf.Max(0, attack.moveConfig.energyCost) : 0;
                return new PlannedCost
                {
                    moveSecs = Mathf.Max(1, moveSecs),
                    atkSecs = 0,
                    moveEnergy = Mathf.Max(0, moveEnergyRate),
                    atkEnergy = 0,
                    valid = true
                };
            }

            return new PlannedCost { valid = true };
        }

        PlannedCost BuildPlannedCost(IActionToolV2 tool, Hex target)
        {
            if (tool is HexClickMover mover)
            {
                var (secs, energy) = mover.GetPlannedCost();
                return new PlannedCost
                {
                    moveSecs = Mathf.Max(0, secs),
                    atkSecs = 0,
                    moveEnergy = Mathf.Max(0, energy),
                    atkEnergy = 0,
                    valid = true,
                    isEnemyTarget = false,
                    isFreeMoveCandidate = false,
                    failReason = null
                };
            }

            if (tool is IAttackPlannerV2 planner)
            {
                var unit = ResolveUnit(tool);
                if (planner.TryGetPlan(unit, target, out var plan, out var reason))
                {
                    return new PlannedCost
                    {
                        moveSecs = Mathf.Max(0, plan.moveSecs),
                        atkSecs = Mathf.Max(0, plan.attackSecs),
                        moveEnergy = Mathf.Max(0, plan.energyMove),
                        atkEnergy = Mathf.Max(0, plan.energyAtk),
                        valid = true,
                        isEnemyTarget = plan.isEnemyTarget,
                        isFreeMoveCandidate = plan.isFreeMoveCandidate,
                        failReason = null
                    };
                }

                return new PlannedCost
                {
                    moveSecs = 0,
                    atkSecs = 0,
                    moveEnergy = 0,
                    atkEnergy = 0,
                    valid = false,
                    isEnemyTarget = false,
                    isFreeMoveCandidate = false,
                    failReason = reason
                };
            }

            return new PlannedCost { valid = true, moveSecs = 0, atkSecs = 0, moveEnergy = 0, atkEnergy = 0, isEnemyTarget = false, isFreeMoveCandidate = false, failReason = null };
        }

        static string MapAimReason(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "notReady";
            raw = raw.ToLowerInvariant();
            if (raw.Contains("no-time"))
                return "lackTime";
            if (raw.Contains("no-energy"))
                return "lackEnergy";
            if (raw.Contains("cooldown"))
                return "cooldown";
            return "notReady";
        }

        static string FormatAttackReject(AttackRejectReasonV2 reason, string message)
            => $"{reason}|{message ?? string.Empty}";

        static string DefaultAttackRejectMessage(AttackRejectReasonV2 reason)
        {
            return reason switch
            {
                AttackRejectReasonV2.NotReady => "Attack not ready.",
                AttackRejectReasonV2.Busy => "Already attacking.",
                AttackRejectReasonV2.OnCooldown => "Attack is on cooldown.",
                AttackRejectReasonV2.NotEnoughResource => "Not enough energy.",
                AttackRejectReasonV2.NoPath => "Can't reach that target.",
                AttackRejectReasonV2.CantMove => "Can't move to attack.",
                _ => "Attack unavailable."
            };
        }

        static void ParseAttackRejectToken(string token, out AttackRejectReasonV2 reason, out string message)
        {
            reason = AttackRejectReasonV2.NoPath;
            message = DefaultAttackRejectMessage(reason);
            if (string.IsNullOrEmpty(token))
                return;

            var parts = token.Split(new[] { '|' }, 2);
            if (parts.Length > 0 && Enum.TryParse(parts[0], out AttackRejectReasonV2 parsed))
                reason = parsed;

            if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                message = parts[1];
            else
                message = DefaultAttackRejectMessage(reason);
        }

        void NotifyAttackRejected(IActionToolV2 tool, Unit unit, string token)
        {
            if (tool is not AttackControllerV2)
                return;

            unit ??= ResolveUnit(tool);
            if (unit == null)
                return;

            ParseAttackRejectToken(token, out var reason, out var message);
            AttackEventsV2.RaiseRejected(unit, reason, message);
        }

        bool IsCooldownReadyForConfirm(IActionToolV2 tool, ICooldownSink sink)
        {
            if (sink == null) return true;
            string skillId = null;

            if (tool is HexClickMover mover && mover.config != null && !string.IsNullOrEmpty(mover.config.actionId))
                skillId = mover.config.actionId;
            else if (tool is AttackControllerV2 attack && attack.attackConfig != null)
                skillId = attack.attackConfig.name;

            if (string.IsNullOrEmpty(skillId))
                return true;

            return sink.Ready(skillId);
        }

        bool IsAnyToolBusy()
        {
            foreach (var mb in tools)
            {
                switch (mb)
                {
                    case HexClickMover mover when mover.IsBusy:
                        return true;
                    case AttackControllerV2 attack when attack.IsBusy:
                        return true;
                }
            }
            return false;
        }

        Hex? PickHexUnderMouse()
        {
            var cam = pickCamera ? pickCamera : Camera.main;
            if (!cam || authoring?.Layout == null) return null;

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, rayMaxDistance, pickMask))
            {
                float t = (pickPlaneY - ray.origin.y) / ray.direction.y;
                if (t < 0) return null;
                hit.point = ray.origin + ray.direction * t;
            }

            var hex = authoring.Layout.HexAt(hit.point);
            return hex;
        }
    }
}