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
            public int secs;
            public int energyMove;
            public int energyAtk;
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
            public int usedSecsMove;
            public int usedSecsAtk;
            public int refundedSecs;
            public int energyMoveNet;
            public int energyAtkNet;
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

            int remain = budget != null ? budget.Remaining : 0;
            int energyBefore = resources != null ? resources.Get("Energy") : 0;

            Log($"[Gate] W2 PreDeduct (move={cost.moveSecs}s/{cost.moveEnergy}, atk={cost.atkSecs}s/{cost.atkEnergy}, total={cost.TotalSeconds}s/{cost.TotalEnergy}, remain={remain}s, energy={energyBefore})");

            string failReason = null;
            if (!cost.valid)
                failReason = "targetInvalid";
            else if (budget != null && cost.TotalSeconds > 0 && !budget.HasTime(cost.TotalSeconds))
                failReason = "lackTime";
            else if (resources != null && cost.TotalEnergy > 0 && !resources.Has("Energy", cost.TotalEnergy))
                failReason = "lackEnergy";
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

            if (budget != null && cost.TotalSeconds > 0)
                budget.SpendTime(cost.TotalSeconds);

            if (resources != null)
            {
                if (cost.moveEnergy > 0)
                    resources.Spend("Energy", cost.moveEnergy, "PreDeduct_Move");
                if (cost.atkEnergy > 0)
                    resources.Spend("Energy", cost.atkEnergy, "PreDeduct_Attack");
            }

            _plan = new PreDeduct
            {
                secs = cost.TotalSeconds,
                energyMove = cost.moveEnergy,
                energyAtk = cost.atkEnergy,
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

            LogExecSummary(unit, plan.kind, report);

            ActionPhaseLogger.Log(unit, plan.kind, "W3_ExecuteEnd");

            Resolve(unit, plan, exec, report, budget, resources);
        }

        void Resolve(Unit unit, ActionPlan plan, IActionExecReportV2 exec, ExecReportData report, ITurnBudget budget, IResourcePool resources)
        {
            int used = Mathf.Max(0, report.usedSecsMove + report.usedSecsAtk);
            int refunded = Mathf.Max(0, report.refundedSecs);
            int net = Mathf.Max(0, used - refunded);
            int energyMove = report.energyMoveNet;
            int energyAtk = report.energyAtkNet;
            bool freeMove = report.freeMoveApplied;

            ActionPhaseLogger.Log(unit, plan.kind, "W4_ResolveBegin", $"(used={used}, refunded={refunded}, net={net}, energyMove={energyMove}, energyAtk={energyAtk})");

            int plannedSecs = _plan.valid ? Mathf.Max(0, _plan.secs) : 0;
            if (budget != null && _plan.valid)
            {
                string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                string reason = freeMove ? "FreeMove" : "Adjust";
                if (net == 0 && refunded > 0 && plannedSecs > 0)
                {
                    budget.RefundTime(plannedSecs);
                    Log($"[Time] Refund {unitLabel} {plannedSecs}s (reason={reason}) -> Remain={budget.Remaining}");
                }
                else
                {
                    int delta = net - plannedSecs;
                    if (delta < 0)
                    {
                        int refundAmount = -delta;
                        if (refundAmount > 0)
                        {
                            budget.RefundTime(refundAmount);
                            Log($"[Time] Refund {unitLabel} {refundAmount}s (reason={reason}) -> Remain={budget.Remaining}");
                        }
                    }
                    else if (delta > 0)
                    {
                        budget.SpendTime(delta);
                        Log($"[Time] Spend {unitLabel} {delta}s -> Remain={budget.Remaining}");
                    }
                }
            }

            if (resources != null)
            {
                string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                int plannedMoveEnergy = _plan.valid ? Mathf.Max(0, _plan.energyMove) : 0;
                int plannedAtkEnergy = _plan.valid ? Mathf.Max(0, _plan.energyAtk) : 0;

                int moveDelta = energyMove - plannedMoveEnergy;
                if (moveDelta != 0)
                {
                    if (moveDelta > 0)
                    {
                        resources.Spend("Energy", moveDelta, "Resolve_Move");
                        Log($"[Res] Spend {unitLabel}:Energy {moveDelta} -> {resources.Get("Energy")} (Move{(freeMove ? "_FreeMove" : string.Empty)})");
                    }
                    else
                    {
                        int refundAmount = -moveDelta;
                        resources.Refund("Energy", refundAmount, "Resolve_Move");
                        Log($"[Res] Refund {unitLabel}:Energy +{refundAmount} -> {resources.Get("Energy")} (Move{(freeMove ? "_FreeMove" : string.Empty)})");
                    }
                }

                int atkDelta = energyAtk - plannedAtkEnergy;
                if (atkDelta != 0)
                {
                    if (atkDelta > 0)
                    {
                        resources.Spend("Energy", atkDelta, "Resolve_Attack");
                        Log($"[Res] Spend {unitLabel}:Energy {atkDelta} -> {resources.Get("Energy")} (Attack)");
                    }
                    else
                    {
                        int refundAmount = -atkDelta;
                        resources.Refund("Energy", refundAmount, "Resolve_Attack");
                        Log($"[Res] Refund {unitLabel}:Energy +{refundAmount} -> {resources.Get("Energy")} (Attack)");
                    }
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
                usedSecsMove = Mathf.Max(0, exec.UsedSeconds),
                usedSecsAtk = 0,
                refundedSecs = Mathf.Max(0, exec.RefundedSeconds),
                energyMoveNet = 0,
                energyAtkNet = 0,
                freeMoveApplied = false
            };

            if (tool is HexClickMover mover)
            {
                data.usedSecsMove = Mathf.Max(0, mover.ReportUsedSeconds);
                data.refundedSecs = Mathf.Max(0, mover.ReportRefundedSeconds);
                data.energyMoveNet = mover.ReportEnergyMoveNet;
                data.energyAtkNet = 0;
                data.freeMoveApplied = mover.ReportFreeMoveApplied;
            }
            else if (tool is AttackControllerV2 attack)
            {
                data.usedSecsMove = Mathf.Max(0, attack.ReportMoveUsedSeconds);
                data.usedSecsAtk = Mathf.Max(0, attack.ReportAttackUsedSeconds);
                data.refundedSecs = Mathf.Max(0, attack.ReportMoveRefundSeconds + attack.ReportAttackRefundSeconds);
                data.energyMoveNet = attack.ReportEnergyMoveNet;
                data.energyAtkNet = attack.ReportEnergyAtkNet;
                data.freeMoveApplied = attack.ReportFreeMoveApplied;
            }

            return data;
        }

        void LogExecSummary(Unit unit, string kind, ExecReportData report)
        {
            string label = TurnManagerV2.FormatUnitLabel(unit);
            string freeMove = report.freeMoveApplied ? " (FreeMove)" : string.Empty;
            if (string.Equals(kind, "Move", System.StringComparison.OrdinalIgnoreCase))
            {
                Log($"[Move] Use secs={report.usedSecsMove}s refund={report.refundedSecs}s energy={report.energyMoveNet} U={label}{freeMove}");
            }
            else if (string.Equals(kind, "Attack", System.StringComparison.OrdinalIgnoreCase))
            {
                Log($"[Attack] Use moveSecs={report.usedSecsMove}s atkSecs={report.usedSecsAtk}s energyMove={report.energyMoveNet} energyAtk={report.energyAtkNet} U={label}{freeMove}");
            }
            else
            {
                Log($"[Action] {label} [{kind}] ExecSummary used={report.usedSecsMove + report.usedSecsAtk}s refund={report.refundedSecs}s energy={report.energyMoveNet + report.energyAtkNet}{freeMove}");
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