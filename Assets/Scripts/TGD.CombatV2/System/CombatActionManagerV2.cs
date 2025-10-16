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

            public int TotalSeconds => Mathf.Max(0, moveSecs + atkSecs);
            public int TotalEnergy => Mathf.Max(0, moveEnergy + atkEnergy);
        }

        struct PreDeduct
        {
            public int moveSecs;
            public int atkSecs;
            public int totalSecs;
            public int energyMove;
            public int energyAtk;
            public int remainBefore;
            public int energyBefore;
            public bool valid;
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
            public int reportedUsed;
            public int reportedRefunded;
            public int usedSecsMove;
            public int usedSecsAtk;
            public int refundedSecsMove;
            public int refundedSecsAtk;
            public bool freeMoveApplied;
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
                    attack.suppressBilling = true;
                    break;
                case HexClickMover mover:
                    mover.AttachTurnManager(turnManager);
                    mover.suppressInternalLogs = quietInternalToolLogs;
                    mover.suppressBilling = true;
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
                CleanupAfterAbort(tool, false);
                yield break;
            }

            if (!TryBeginAim(tool, unit, out _, false))
            {
                ActionPhaseLogger.Log(unit, kind, "W2_PrecheckOk");
                ActionPhaseLogger.Log(unit, kind, "W2_PreDeductCheckFail", "(reason=notReady)");
                ActionPhaseLogger.Log(unit, kind, "W2_ConfirmAbort", "(reason=notReady)");
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

            int pm = Mathf.Max(0, cost.moveSecs);
            int pa = Mathf.Max(0, cost.atkSecs);
            int totalSecs = Mathf.Max(0, pm + pa);
            int em = Mathf.Max(0, cost.moveEnergy);
            int ea = Mathf.Max(0, cost.atkEnergy);

            int remain = budget != null ? budget.Remaining : 0;
            int energyBefore = resources != null ? resources.Get("Energy") : 0;

            Log($"[Gate] W2 PreDeduct (move={pm}s/{em}, atk={pa}s/{ea}, total={totalSecs}s/{em + ea}, remain={remain}s, energy={energyBefore})");

            string failReason = null;
            if (!cost.valid)
                failReason = "targetInvalid";
            else if (budget != null && totalSecs > 0 && remain < totalSecs)
                failReason = "lack";
            else if (resources != null && (em + ea) > 0 && energyBefore < (em + ea))
                failReason = "lack";
            else if (!IsCooldownReadyForConfirm(tool, cooldowns))
                failReason = "cooldown";

            if (failReason != null)
            {
                ActionPhaseLogger.Log(unit, kind, "W2_PreDeductCheckFail", $"(reason={failReason})");
                ActionPhaseLogger.Log(unit, kind, "W2_ConfirmAbort", $"(reason={failReason})");
                CleanupAfterAbort(tool, false);
                yield break;
            }

            ActionPhaseLogger.Log(unit, kind, "W2_PreDeductCheckOk");

            if (budget != null && totalSecs > 0)
                budget.SpendTime(totalSecs);

            if (resources != null)
            {
                if (em > 0)
                    resources.Spend("Energy", em, "PreDeduct_Move");
                if (ea > 0)
                    resources.Spend("Energy", ea, "PreDeduct_Attack");
            }

            _plan = new PreDeduct
            {
                moveSecs = pm,
                atkSecs = pa,
                totalSecs = totalSecs,
                energyMove = em,
                energyAtk = ea,
                remainBefore = remain,
                energyBefore = energyBefore,
                valid = true
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

            int budgetBefore = budget != null ? budget.Remaining : 0;
            int energyBefore = resources != null ? resources.Get("Energy") : 0;
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

            LogExecSummary(unit, plan, report);

            ActionPhaseLogger.Log(unit, plan.kind, "W3_ExecuteEnd");

            Resolve(unit, plan, exec, report, budget, resources);
        }

        void Resolve(Unit unit, ActionPlan plan, IActionExecReportV2 exec, ExecReportData report, ITurnBudget budget, IResourcePool resources)
        {
            int plannedMove = _plan.valid ? Mathf.Max(0, _plan.moveSecs) : Mathf.Max(0, plan.cost.moveSecs);
            int plannedAtk = _plan.valid ? Mathf.Max(0, _plan.atkSecs) : Mathf.Max(0, plan.cost.atkSecs);
            int plannedTotal = Mathf.Max(0, plannedMove + plannedAtk);
            int plannedMoveEnergy = _plan.valid ? Mathf.Max(0, _plan.energyMove) : Mathf.Max(0, plan.cost.moveEnergy);
            int plannedAtkEnergy = _plan.valid ? Mathf.Max(0, _plan.energyAtk) : Mathf.Max(0, plan.cost.atkEnergy);
            int remainBefore = _plan.valid ? _plan.remainBefore : (budget != null ? budget.Remaining + plannedTotal : 0);

            int used = plannedTotal > 0 ? Mathf.Clamp(report.reportedUsed, 0, plannedTotal) : Mathf.Max(0, report.reportedUsed);
            int refunded = plannedTotal > 0 ? Mathf.Clamp(report.reportedRefunded, 0, used) : Mathf.Min(Mathf.Max(0, report.reportedRefunded), used);
            bool freeMove = report.freeMoveApplied && plannedMove > 0;

            if (freeMove)
            {
                refunded = Mathf.Clamp(refunded + 1, 0, used);
            }

            int net = Mathf.Max(0, used - refunded);
            int refundTime = Mathf.Max(0, plannedTotal - net);

            int usedMoveClamped = plannedMove > 0 ? Mathf.Clamp(report.usedSecsMove, 0, plannedMove) : 0;
            int refundedMoveClamped = plannedMove > 0 ? Mathf.Clamp(report.refundedSecsMove, 0, usedMoveClamped) : 0;
            int netMoveSeconds = Mathf.Max(0, usedMoveClamped - refundedMoveClamped);
            if (freeMove && netMoveSeconds > 0)
                netMoveSeconds = Mathf.Max(0, netMoveSeconds - 1);

            int refundedAtkRaw = Mathf.Max(0, report.refundedSecsAtk);
            bool attackAdjusted = plannedAtk > 0 && report.usedSecsAtk <= 0 && refundedAtkRaw >= plannedAtk;

            string reason = freeMove ? "FreeMove" : (attackAdjusted ? "Attack_Adjust" : "Refund");

            int moveRate = plannedMove > 0 ? Mathf.RoundToInt((float)plannedMoveEnergy / Mathf.Max(1, plannedMove)) : 0;
            int refundMoveEnergy = Mathf.Clamp(plannedMoveEnergy - netMoveSeconds * moveRate, 0, plannedMoveEnergy);

            string energyMoveDesc = refundMoveEnergy > 0 ? $"{plannedMoveEnergy}-{refundMoveEnergy}" : plannedMoveEnergy.ToString();
            string energyAtkDesc = attackAdjusted ? (-plannedAtkEnergy).ToString() : plannedAtkEnergy.ToString();

            ActionPhaseLogger.Log(unit, plan.kind, "W4_ResolveBegin", $"(used={used}, refunded={refunded}, net={net}, energyMove={energyMoveDesc}, energyAtk={energyAtkDesc})");

            if (budget != null && plannedTotal > 0)
            {
                string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                if (refundTime > 0)
                {
                    budget.RefundTime(refundTime);
                    int remainAfter = remainBefore - plannedTotal + refundTime;
                    Log($"[Time] Refund {unitLabel} {refundTime}s (reason={reason}) -> Remain={remainAfter}");
                }
            }

            if (resources != null)
            {
                string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                if (refundMoveEnergy > 0)
                {
                    resources.Refund("Energy", refundMoveEnergy, freeMove ? "Resolve_Move_FreeMove" : "Resolve_Move");
                    Log($"[Res] Refund {unitLabel}:Energy +{refundMoveEnergy} -> {resources.Get("Energy")} (Move{(freeMove ? "_FreeMove" : string.Empty)})");
                }

                if (attackAdjusted && plannedAtkEnergy > 0)
                {
                    resources.Refund("Energy", plannedAtkEnergy, "Resolve_Attack_Adjust");
                    Log($"[Res] Refund {unitLabel}:Energy +{plannedAtkEnergy} -> {resources.Get("Energy")} (Attack_Adjust)");
                }
            }

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
                reportedUsed = Mathf.Max(0, exec.UsedSeconds),
                reportedRefunded = Mathf.Max(0, exec.RefundedSeconds),
                usedSecsMove = 0,
                usedSecsAtk = 0,
                refundedSecsMove = 0,
                refundedSecsAtk = 0,
                freeMoveApplied = false
            };

            if (tool is HexClickMover mover)
            {
                data.usedSecsMove = Mathf.Max(0, mover.ReportUsedSeconds);
                data.refundedSecsMove = Mathf.Max(0, mover.ReportRefundedSeconds);
                data.freeMoveApplied = mover.ReportFreeMoveApplied;
            }
            else if (tool is AttackControllerV2 attack)
            {
                data.usedSecsMove = Mathf.Max(0, attack.ReportMoveUsedSeconds);
                data.usedSecsAtk = Mathf.Max(0, attack.ReportAttackUsedSeconds);
                data.refundedSecsMove = Mathf.Max(0, attack.ReportMoveRefundSeconds);
                data.refundedSecsAtk = Mathf.Max(0, attack.ReportAttackRefundSeconds);
                data.freeMoveApplied = attack.ReportFreeMoveApplied;
            }

            return data;
        }

        void LogExecSummary(Unit unit, ActionPlan plan, ExecReportData report)
        {
            string label = TurnManagerV2.FormatUnitLabel(unit);
            string freeMove = report.freeMoveApplied ? " (FreeMove)" : string.Empty;
            int plannedMoveEnergy = Mathf.Max(0, plan.cost.moveEnergy);
            int plannedAtkEnergy = Mathf.Max(0, plan.cost.atkEnergy);

            if (string.Equals(plan.kind, "Move", System.StringComparison.OrdinalIgnoreCase))
            {
                Log($"[Move] Use secs={report.usedSecsMove}s refund={report.refundedSecsMove}s energy={plannedMoveEnergy} U={label}{freeMove}");
            }
            else if (string.Equals(plan.kind, "Attack", System.StringComparison.OrdinalIgnoreCase))
            {
                Log($"[Attack] Use moveSecs={report.usedSecsMove}s atkSecs={report.usedSecsAtk}s energyMove={plannedMoveEnergy} energyAtk={plannedAtkEnergy} U={label}{freeMove}");
            }
            else
            {
                int totalUsed = Mathf.Max(0, report.reportedUsed);
                int totalRefunded = Mathf.Max(0, report.reportedRefunded);
                int totalEnergy = Mathf.Max(0, plan.cost.TotalEnergy);
                Log($"[Action] {label} [{plan.kind}] ExecSummary used={totalUsed}s refund={totalRefunded}s energy={totalEnergy}{freeMove}");
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
                    valid = true
                };
            }

            if (tool is AttackControllerV2 attack)
            {
                var planned = attack.PeekPlannedCost(target);
                return new PlannedCost
                {
                    moveSecs = Mathf.Max(0, planned.moveSecs),
                    atkSecs = Mathf.Max(0, planned.atkSecs),
                    moveEnergy = Mathf.Max(0, planned.moveEnergy),
                    atkEnergy = Mathf.Max(0, planned.atkEnergy),
                    valid = planned.valid
                };
            }

            return new PlannedCost { valid = true };
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