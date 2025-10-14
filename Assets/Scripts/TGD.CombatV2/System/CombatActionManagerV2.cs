using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public enum ActionModeV2 { Idle, MoveAim, AttackAim, ChainPrompt, Busy }
    public enum ChainPromptAutoMode
    {
        Skip,
        FirstAvailable
    }

    public enum ChainTestMode
    {
        Skip,
        AutoFirst
    }

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
        public List<MonoBehaviour> tools = new();  // 拖 ClickMover, AttackControllerV2 等

        [Header("Chain Prompt")]
        public ChainPromptAutoMode chainPromptAuto = ChainPromptAutoMode.Skip;

        [Header("Test Hooks")]
        public ChainTestMode chainTestMode = ChainTestMode.Skip;

        [Header("Keybinds")]
        public KeyCode keyMoveAim = KeyCode.M;
        public KeyCode keyAttackAim = KeyCode.A;
        public KeyCode keyFullRound = KeyCode.F;

        ActionModeV2 _mode = ActionModeV2.Idle;
        readonly Dictionary<string, List<IActionToolV2>> _toolsById = new();
        Unit _currentUnit; // ★ 记录当前回合单位
        IActionToolV2 _activeTool;
        Hex? _hover;
        bool _turnJustStarted;
        readonly List<IActionToolV2> _chainCandidates = new();
        enum PlanShortageReason
        {
            None,
            Time,
            Energy
        }

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
                    break;
                case HexClickMover mover:
                    mover.AttachTurnManager(turnManager);
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
            if (turnManager != null)
            {
                turnManager.TurnStarted += OnTurnStarted;
                turnManager.FullRoundExecuteRequested += OnFullRoundExecuteRequested;
            }
        }
        void OnDisable()
        {
            if (turnManager != null)
            {
                turnManager.TurnStarted -= OnTurnStarted;
                turnManager.FullRoundExecuteRequested -= OnFullRoundExecuteRequested;
            }
        }
        void OnTurnStarted(TGD.HexBoard.Unit u)
        {
            _currentUnit = u;
            _turnJustStarted = true;
            if (_activeTool != null && ResolveUnit(_activeTool) != _currentUnit)
                Cancel();
        }

        // —— 选择“属于当前回合单位”的工具 —— 
        IActionToolV2 SelectTool(string id)
        {
            if (!_toolsById.TryGetValue(id, out var list)) return null;
            foreach (var t in list)
                if (ResolveUnit(t) == _currentUnit)
                    return t;
            return null;
        }

        void Update()
        {
            // —— 模式切换（互斥）——
            if (_mode != ActionModeV2.Busy)
            {
                if (Input.GetKeyDown(keyMoveAim)) RequestAim("Move");
                if (Input.GetKeyDown(keyAttackAim)) RequestAim("Attack");
                if (Input.GetKeyDown(keyFullRound)) RequestAim("FullRound_Test");
            }

            // —— 瞄准中：Hover / Confirm / Cancel —— 
            if (_mode == ActionModeV2.MoveAim || _mode == ActionModeV2.AttackAim)
            {
                var h = PickHexUnderMouse();
                if (h.HasValue && (!_hover.HasValue || !_hover.Value.Equals(h.Value)))
                {
                    _hover = h;
                    _activeTool?.OnHover(h.Value);
                }

                if (Input.GetMouseButtonDown(0)) Confirm();      // 左键确认
                if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) Cancel(true); // 右键/ESC 取消
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

        bool Precheck(Unit unit, IActionToolV2 tool)
        {
            if (turnManager == null || unit == null) return true;
            if (_currentUnit != null && unit != _currentUnit)
                return false;
            return true;
        }
        bool PrecheckLight(IActionToolV2 tool, Unit unit, out string reason)
        {
            reason = null;
            if (tool == null)
                return false;

            if (tool.Kind == ActionKind.FullRound)
            {
                if (!_turnJustStarted)
                {
                    Debug.Log("[FullRound] reject not-full-time", this);
                    reason = "(NotFullTime)";
                    return false;
                }

                if (turnManager == null || unit == null)
                {
                    Debug.Log("[FullRound] reject not-full-time", this);
                    reason = "(NotFullTime)";
                    return false;
                }

                var budget = turnManager.GetBudget(unit);
                int turnTime = turnManager.GetTurnTime(unit);
                if (budget == null || budget.Remaining < turnTime)
                {
                    Debug.Log("[FullRound] reject not-full-time", this);
                    reason = "(NotFullTime)";
                    return false;
                }
            }
            switch (tool)
            {
                case HexClickMover mover:
                    return mover.TryPrecheckAim(out reason);
                case AttackControllerV2 attack:
                    return attack.TryPrecheckAim(out reason);
            }

            if (turnManager != null && unit != null)
            {
                var budget = turnManager.GetBudget(unit);
                if (budget != null && budget.Remaining <= 0)
                {
                    reason = "(no-time)";
                    return false;
                }
                var resources = turnManager.GetResources(unit);
                if (resources != null && resources.Get("Energy") <= 0)
                {
                    reason = "(no-energy)";
                    return false;
                }
            }

            return true;
        }

        (int budgetAfter, int energyAfter) ApplyExecution(
        Unit unit,
        IActionToolV2 tool,
        ActionCostPlan plan,
        int usedSeconds,
        int refundedSeconds,
        int moveEnergyActual,
        int attackEnergyActual)
        {
            if (tool is not IActionExecReportV2 exec)
                return (-1, -1);
            int used = Mathf.Max(0, usedSeconds);
            int refunded = Mathf.Max(0, refundedSeconds);
            int moveEnergy = moveEnergyActual;
            int attackEnergy = attackEnergyActual;
            int totalEnergyActual = moveEnergy + attackEnergy;
            int planTime = Mathf.Max(0, plan.timeSeconds);
            int planEnergyTotal = Mathf.Max(0, plan.energy);

            int extraUsed = Mathf.Max(0, used - planTime);
            int consumedAfterRefund = Mathf.Max(0, planTime - refunded);
            int finalTimeSpent = consumedAfterRefund + extraUsed;
            int timeDelta = finalTimeSpent - planTime;
            int energyDelta = totalEnergyActual - planEnergyTotal;

            int budgetAfter = -1;
            int energyAfter = -1;

            var attackTool = tool as AttackControllerV2;
            bool freeMove = attackTool != null && attackTool.FreeMoveApplied;

            if (turnManager != null && unit != null)
            {
                var budget = turnManager.GetBudget(unit);
                if (budget != null)
                {
                    if (timeDelta > 0)
                    {
                        budget.SpendTime(timeDelta);
                    }
                    else if (timeDelta < 0)
                        budget.RefundTime(-timeDelta);

                    budgetAfter = budget.Remaining;
                    string timeLog;
                    if (timeDelta > 0)
                        timeLog = $"[Time] Spend {timeDelta}s -> Remain={budget.Remaining}";
                    else if (timeDelta < 0)
                    {
                        int refundAmount = -timeDelta;
                        string suffix = freeMove ? " (FreeMove)" : string.Empty;
                        timeLog = $"[Time] Refund {refundAmount}s -> Remain={budget.Remaining}{suffix}";
                    }
                    else
                        timeLog = $"[Time] NoChange -> Remain={budget.Remaining}";
                    Debug.Log(timeLog, this);
                }

                var resources = turnManager.GetResources(unit);
                if (resources != null)
                {
                    if (energyDelta > 0)
                        resources.Spend("Energy", energyDelta, tool.Id);
                    else if (energyDelta < 0)
                        resources.Refund("Energy", -energyDelta, tool.Id);
                    energyAfter = resources.Get("Energy");
                    string resLog;
                    if (energyDelta > 0)
                        resLog = $"[Res] Spend {energyDelta} -> {energyAfter}";
                    else if (energyDelta < 0)
                        resLog = $"[Res] Refund {-energyDelta} -> {energyAfter}";
                    else
                        resLog = $"[Res] NoChange -> {energyAfter}";
                    Debug.Log(resLog, this);
                }
            }
            exec.Consume();
            return (budgetAfter, energyAfter);
        }

        (int move, int attack) ResolveActualEnergyDetail(IActionToolV2 tool)
        {
            if (tool is IActionEnergyReportV2 energyReport)
                return (
                    energyReport.ReportMoveEnergyNet,
                    energyReport.ReportAttackEnergyNet);
            return (0, 0);
        }

        ActionCostPlan GetPlannedCost(IActionToolV2 tool, Hex hex)
        {
            if (tool == null)
                return ActionCostPlan.Invalid();

            try
            {
                return tool.PlannedCost(hex);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Action] PlannedCost error: {tool.Id} {ex.Message}", this);
                return ActionCostPlan.Invalid("(plan-error)");
            }
        }

        bool ValidatePlan(
         Unit unit,
         ActionCostPlan plan,
         out string okMessage,
         out string failReason,
         out PlanShortageReason shortage,
         out int timeRemainBefore,
         out int energyRemainBefore)
        {
            okMessage = string.Empty;
            failReason = "(reason=lack)";
            shortage = PlanShortageReason.None;
            timeRemainBefore = -1;
            energyRemainBefore = -1;

            if (!plan.valid)
            {
                failReason = string.IsNullOrEmpty(plan.detail) ? "(reason=unreachable)" : plan.detail;
                return false;
            }

            ITurnBudget budget = null;
            IResourcePool resources = null;

            if (turnManager != null && unit != null)
            {
                budget = turnManager.GetBudget(unit);
                if (budget != null)
                    timeRemainBefore = budget.Remaining;

                resources = turnManager.GetResources(unit);
                if (resources != null)
                    energyRemainBefore = resources.Get("Energy");
            }

            if (budget != null && plan.timeSeconds > 0 && timeRemainBefore >= 0 && timeRemainBefore < plan.timeSeconds)
            {
                shortage = PlanShortageReason.Time;
                failReason = "(reason=lack)";
                return false;
            }

            if (resources != null && plan.energy > 0 && energyRemainBefore >= 0 && energyRemainBefore < plan.energy)
            {
                shortage = PlanShortageReason.Energy;
                failReason = "(reason=lack)";
                return false;
            }

            List<string> segments = new();
            if (plan.primaryTimeSeconds > 0)
                segments.Add($"move={plan.primaryTimeSeconds}s");
            if (plan.secondaryTimeSeconds > 0)
                segments.Add($"atk={plan.secondaryTimeSeconds}s");
            segments.Add($"total={plan.timeSeconds}s");

            if (budget != null && timeRemainBefore >= 0)
            {
                int remainAfter = Mathf.Max(0, timeRemainBefore - plan.timeSeconds);
                segments.Add($"remain={remainAfter}s");
            }

            if (plan.secondaryEnergy > 0 && plan.primaryEnergy > 0)
                segments.Add($"energy={plan.primaryEnergy}+{plan.secondaryEnergy}={plan.energy}");
            else
                segments.Add($"energy={plan.energy}");

            if (resources != null && energyRemainBefore >= 0)
            {
                int energyAfter = Mathf.Max(0, energyRemainBefore - plan.energy);
                segments.Add($"energyRemain={energyAfter}");
            }

            okMessage = $"({string.Join(", ", segments)})";
            return true;
        }
        static string FormatBudgetValue(int value) => value >= 0 ? value.ToString() : "?";

        void LogW2PreDeduct(ActionCostPlan plan, int timeRemainBefore, int energyRemainBefore)
        {
            Debug.Log($"[Gate] W2 PreDeduct planSecs={plan.timeSeconds} planEnergyMove={plan.primaryEnergy} planEnergyAtk={plan.secondaryEnergy} before=Time:{FormatBudgetValue(timeRemainBefore)}/Energy:{FormatBudgetValue(energyRemainBefore)}", this);
        }

        void LogW2PreDeductSuccess(ActionCostPlan plan, int timeRemainBefore, int energyRemainBefore)
        {
            int afterTime = timeRemainBefore >= 0 ? Mathf.Max(0, timeRemainBefore - plan.timeSeconds) : -1;
            int afterEnergy = energyRemainBefore >= 0 ? Mathf.Max(0, energyRemainBefore - plan.energy) : -1;
            Debug.Log($"[Gate] W2 PreDeduct OK -> after=Time:{FormatBudgetValue(afterTime)}/Energy:{FormatBudgetValue(afterEnergy)}", this);
        }

        void LogW2PreDeductAbort(ActionCostPlan plan, PlanShortageReason shortage, int timeRemainBefore, int energyRemainBefore)
        {
            if (shortage == PlanShortageReason.None)
                return;
            int need = shortage == PlanShortageReason.Time ? plan.timeSeconds : plan.energy;
            int leftRaw = shortage == PlanShortageReason.Time ? timeRemainBefore : energyRemainBefore;
            int left = leftRaw >= 0 ? leftRaw : 0;
            string reason = shortage == PlanShortageReason.Time ? "time" : "energy";
            Debug.Log($"[Gate] W2 PreDeduct Abort (reason=lack) need={need} left={left} type={reason}", this);
        }

        void DeductPlan(Unit unit, IActionToolV2 tool, ActionCostPlan plan)
        {
            if (turnManager != null && unit != null)
            {
                var budget = turnManager.GetBudget(unit);
                if (budget != null && plan.timeSeconds > 0)
                    budget.SpendTime(plan.timeSeconds);

                var resources = turnManager.GetResources(unit);
                if (resources != null && plan.energy > 0)
                    resources.Spend("Energy", plan.energy, $"{tool.Id}_Plan");
            }

            _turnJustStarted = false;
        }

        IEnumerator ExecuteAction(Unit unit, IActionToolV2 tool, Hex hex, ActionCostPlan plan, bool exitActiveTool)
        {
            _mode = ActionModeV2.Busy;
            _hover = null;

            int budgetBefore = -1;
            int energyBefore = -1;
            if (turnManager != null && unit != null)
            {
                var budget = turnManager.GetBudget(unit);
                if (budget != null)
                    budgetBefore = budget.Remaining;

                var resources = turnManager.GetResources(unit);
                if (resources != null)
                    energyBefore = resources.Get("Energy");
            }

            ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W3_ExecuteBegin, $"(budgetBefore={FormatBudgetValue(budgetBefore)}, energyBefore={FormatBudgetValue(energyBefore)})");

            var skippable = tool as IBudgetGateSkippable;
            if (skippable != null)
                skippable.SkipBudgetGate = true;

            IEnumerator routine = null;
            try
            {
                routine = tool?.OnConfirm(hex);
            }
            catch
            {
                if (skippable != null)
                    skippable.SkipBudgetGate = false;
                throw;
            }

            if (routine != null)
                yield return RunToolRoutine(routine, skippable);
            else if (skippable != null)
                skippable.SkipBudgetGate = false;
            ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W3_ExecuteEnd);
            int used = 0;
            int refunded = 0;
            int moveEnergyActual = 0;
            int attackEnergyActual = 0;
            if (tool is IActionExecReportV2 execReport)
            {
                used = Mathf.Max(0, execReport.UsedSeconds);
                refunded = Mathf.Max(0, execReport.RefundedSeconds);
                (moveEnergyActual, attackEnergyActual) = ResolveActualEnergyDetail(tool);
            }

            int net = Mathf.Max(0, used - refunded);
            string resolveBegin = $"(used={used}, refunded={refunded}, net={net}, energyMove={moveEnergyActual}, energyAtk={attackEnergyActual}";
            if (tool is AttackControllerV2 atk && atk.FreeMoveApplied)
                resolveBegin += " [FreeMove]";
            resolveBegin += ")";
            ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W4_ResolveBegin, resolveBegin);
            var (budgetAfter, energyAfter) = ApplyExecution(unit, tool, plan, used, refunded, moveEnergyActual, attackEnergyActual);
            ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W4_ResolveEnd, $"(budgetAfter={FormatBudgetValue(budgetAfter)}, energyAfter={FormatBudgetValue(energyAfter)})");
            if (exitActiveTool)
            {
                if (_activeTool == tool)
                    ExitActiveTool(false);
                else
                    tool?.OnExitAim();
            }
            else
            {
                tool?.OnExitAim();
            }
        }
        IEnumerator RunToolRoutine(IEnumerator routine, IBudgetGateSkippable skippable)
        {
            if (routine == null)
            {
                if (skippable != null)
                    skippable.SkipBudgetGate = false;
                yield break;
            }

            try
            {
                while (routine.MoveNext())
                    yield return routine.Current;
            }
            finally
            {
                if (routine is IDisposable disposable)
                    disposable.Dispose();
                if (skippable != null)
                    skippable.SkipBudgetGate = false;
            }
        }

        IEnumerator ChainPromptOneHop(Unit unit, IActionToolV2 baseTool, Hex hex)
        {
            _chainCandidates.Clear();
            if (baseTool == null)
                yield break;

            foreach (var kv in _toolsById)
            {
                var list = kv.Value;
                if (list == null) continue;
                foreach (var candidate in list)
                {
                    if (candidate == null || candidate == baseTool)
                        continue;
                    if (ResolveUnit(candidate) != unit)
                        continue;
                    switch (candidate.Kind)
                    {
                        case ActionKind.Derived when candidate.CanChainAfter(baseTool.Id, baseTool.ChainTags):
                            _chainCandidates.Add(candidate);
                            break;
                        case ActionKind.Free:
                            _chainCandidates.Add(candidate);
                            break;
                    }
                }
            }

            _mode = ActionModeV2.ChainPrompt;
            ActionPhaseLogger.Log(unit, baseTool.Id, ActionPhase.W2_ChainPromptOpen, $"(count={_chainCandidates.Count})");

            IActionToolV2 selected = null;
            bool aborted = false;
            string abortReason = "(auto-skip)";

            while (_mode == ActionModeV2.ChainPrompt)
            {
                yield return null;

                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                {
                    aborted = true;
                    abortReason = "(esc)";
                    break;
                }

                var promptMode = chainPromptAuto;
                switch (chainTestMode)
                {
                    case ChainTestMode.Skip:
                        promptMode = ChainPromptAutoMode.Skip;
                        break;
                    case ChainTestMode.AutoFirst:
                        promptMode = ChainPromptAutoMode.FirstAvailable;
                        break;
                }

                if (_chainCandidates.Count == 0 || promptMode == ChainPromptAutoMode.Skip)
                {
                    aborted = true;
                    abortReason = "(auto-skip)";
                    break;
                }

                selected = _chainCandidates[0];
                break;
            }

            if (aborted || selected == null)
            {
                ActionPhaseLogger.Log(unit, baseTool.Id, ActionPhase.W2_ChainPromptAbort, abortReason);
                _mode = ActionModeV2.Busy;
                yield break;
            }

            ActionPhaseLogger.Log(unit, selected.Id, ActionPhase.W2_1_Select, $"(id={selected.Id}, kind={selected.Kind})");

            var plan = GetPlannedCost(selected, hex);
            var planValid = ValidatePlan(
            unit,
            plan,
            out var okMessage,
            out var failReason,
            out var shortage,
            out var timeRemainBefore,
            out var energyRemainBefore);
            LogW2PreDeduct(plan, timeRemainBefore, energyRemainBefore);
            if (!planValid)
            {
                LogW2PreDeductAbort(plan, shortage, timeRemainBefore, energyRemainBefore);
                ActionPhaseLogger.Log(unit, selected.Id, ActionPhase.W2_1_PreDeductCheckFail, failReason);
                LogW2PreDeductSuccess(plan, timeRemainBefore, energyRemainBefore);
                _mode = ActionModeV2.Busy;
                yield break;
            }

            ActionPhaseLogger.Log(unit, selected.Id, ActionPhase.W2_1_PreDeductCheckOk, okMessage);
            DeductPlan(unit, selected, plan);

            _mode = ActionModeV2.Busy;
            yield return ExecuteAction(unit, selected, hex, plan, false);
        }

        void HandleFullRoundDeclaration(Unit unit, IActionToolV2 tool, Hex hex, ActionCostPlan plan)
        {
            ActionPhaseLogger.LogFullRoundDeclared(unit, tool.Id, 1);
            _hover = null;

            if (turnManager != null && unit != null)
            {
                turnManager.EnqueueFullRound(new TurnManagerV2.FullRoundQueuedAction(unit, tool, hex, plan, true));
                turnManager.EndTurn(unit);
            }

            if (_activeTool == tool)
                ExitActiveTool(false);
            else
                tool?.OnExitAim();
            _mode = ActionModeV2.Idle;
        }

        IEnumerator OnFullRoundExecuteRequested(TurnManagerV2.FullRoundQueuedAction entry)
        {
            if (entry.tool == null)
                yield break;

            var unit = entry.unit;
            ActionPhaseLogger.LogFullRoundExecute(unit, entry.tool.Id);
            yield return ExecuteAction(unit, entry.tool, entry.hex, entry.plan, false);
            _mode = ActionModeV2.Idle;
        }

        // ===== 外部 UI 也可以直接用这俩 API =====
        public void RequestAim(string toolId)
        {
            if (_mode == ActionModeV2.Busy || _mode == ActionModeV2.ChainPrompt) return;

            var tool = SelectTool(toolId);
            if (tool == null) return;

            if (_activeTool == tool) { Cancel(); return; }
            var unit = ResolveUnit(tool);
            if (!PrecheckLight(tool, unit, out var reason))
            {
                ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W1_AimRejected, reason);
                return;
            }

            if (_activeTool != null) Cancel();

            _activeTool = tool;
            _hover = null;
            _activeTool.OnEnterAim();
            _mode = (toolId == "Attack") ? ActionModeV2.AttackAim : ActionModeV2.MoveAim;
            ActionPhaseLogger.Log(ResolveUnit(tool), tool.Id, ActionPhase.W1_AimBegin);
        }

        void ExitActiveTool(bool userInitiated)
        {
            if (_activeTool != null)
            {
                _activeTool.OnExitAim();
                _activeTool = null;
            }
            _hover = null;
            _mode = ActionModeV2.Idle;
        }
        void AbortConfirm(IActionToolV2 tool, Unit unit, string reason)
        {
            ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W2_ConfirmAbort, reason);
            if (tool is AttackControllerV2 attackTool)
                attackTool.OnConfirmAbortCleanup();
            if (_activeTool == tool)
            {
                ExitActiveTool(false);
            }
            else
            {
                tool?.OnExitAim();
                _hover = null;
                _mode = ActionModeV2.Idle;
            }
        }

        public void Cancel(bool userInitiated = false)
        {
            if (_activeTool != null && (_mode == ActionModeV2.MoveAim || _mode == ActionModeV2.AttackAim))
                ActionPhaseLogger.Log(ResolveUnit(_activeTool), _activeTool.Id, ActionPhase.W1_AimCancel);
            if (_activeTool is AttackControllerV2 attack)
                attack.OnAimCancelCleanup();
            ExitActiveTool(userInitiated);
        }

        public void Confirm()
        {
            if (_activeTool == null) return;
            var unit = ResolveUnit(_activeTool);
            var h = _hover ?? PickHexUnderMouse();
            if (!h.HasValue)
            {
                ActionPhaseLogger.Log(unit, _activeTool.Id, ActionPhase.W2_TargetInvalid, "(no-target)");
                ActionPhaseLogger.Log(unit, _activeTool.Id, ActionPhase.W2_ConfirmAbort, "(no-target)");
                return;
            }

            StartCoroutine(RunW2(_activeTool, h.Value, unit));
        }

        IEnumerator RunW2(IActionToolV2 tool, Hex hex, Unit unit)
        {
            ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W2_ConfirmStart);

            if (!Precheck(unit, tool))
            {
                AbortConfirm(tool, unit, "(precheck)");
                yield break;
            }
            ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W2_PrecheckOk);

            var plan = GetPlannedCost(tool, hex);
            var planValid = ValidatePlan(
               unit,
               plan,
               out var okMessage,
               out var failReason,
               out var shortage,
               out var timeRemainBefore,
               out var energyRemainBefore);
            LogW2PreDeduct(plan, timeRemainBefore, energyRemainBefore);
            if (!planValid)
            {
                LogW2PreDeductAbort(plan, shortage, timeRemainBefore, energyRemainBefore);
                ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W2_PreDeductCheckFail, failReason);
                AbortConfirm(tool, unit, failReason);
                yield break;
            }

            ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W2_PreDeductCheckOk, okMessage);
            LogW2PreDeductSuccess(plan, timeRemainBefore, energyRemainBefore);
            DeductPlan(unit, tool, plan);

            if (tool.Kind == ActionKind.FullRound)
            {
                HandleFullRoundDeclaration(unit, tool, hex, plan);
                yield break;
            }

            yield return ChainPromptOneHop(unit, tool, hex);

            yield return ExecuteAction(unit, tool, hex, plan, true);
        }

        // ===== 拾取统一在 Manager 做，一处修就全修 =====
        Hex? PickHexUnderMouse()
        {
            var cam = pickCamera ? pickCamera : Camera.main;
            if (!cam || authoring?.Layout == null) return null;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, rayMaxDistance, pickMask, QueryTriggerInteraction.Ignore))
                return authoring.Layout.HexAt(hit.point);

            var plane = new Plane(Vector3.up, new Vector3(0f, pickPlaneY, 0f));
            if (!plane.Raycast(ray, out float dist)) return null;
            return authoring.Layout.HexAt(ray.GetPoint(dist));
        }
    }
}
