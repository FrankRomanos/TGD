using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TGD.CombatV2.Targeting;
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
        public HexBoardTiler tiler;

        [Header("Chain Cursor Colors")]
        public Color chainValidColor = new(1f, 0.9f, 0.2f, 0.85f);
        public Color chainInvalidColor = new(1f, 0.3f, 0.3f, 0.7f);

        [Header("Turn Runtime")]
        public TurnManagerV2 turnManager;
        public HexBoardTestDriver unitDriver;

        [SerializeField]
        [Tooltip("Only one CAM should register phase/turn gates in a scene.")]
        bool registerAsGateHub = true;

        [Header("Tools (drag any components that implement IActionToolV2)")]
        public List<MonoBehaviour> tools = new();

        [Header("Keybinds")]
        public KeyCode keyMoveAim = KeyCode.V;
        public KeyCode keyAttackAim = KeyCode.A;

        [Header("Rulebook")]
        public ActionRulebook rulebook;

        [System.Serializable]
        public struct ChainKeybind
        {
            public string id;
            public KeyCode key;
        }

        [Header("Chain Keybinds")]
        public List<ChainKeybind> chainKeybinds = new();

        public bool debugLog = true;
        public bool quietInternalToolLogs = true;

        [Header("Phase Start Free Chain")]
        public bool skipPhaseStartFreeChain = false;

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
        TargetSelectionCursor _chainCursor;
        IHexHighlighter _aimHighlighter;
        IHexHighlighter _chainHighlighter;
        int _inputSuppressionDepth;

        bool IsInputSuppressed => _inputSuppressionDepth > 0;

        void PushInputSuppression()
        {
            _inputSuppressionDepth++;
        }

        void PopInputSuppression()
        {
            if (_inputSuppressionDepth > 0)
                _inputSuppressionDepth--;
        }

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

        struct ChainOption
        {
            public IActionToolV2 tool;
            public KeyCode key;
            public int secs;
            public int energy;
            public ActionKind kind;
        }

        struct ChainQueueOutcome
        {
            public bool queued;
            public bool cancel;
            public IActionToolV2 tool;
        }

        struct ExecReportData
        {
            public bool valid;
            public int plannedSecsMove;
            public int plannedSecsAtk;
            public int refundedSecsMove;
            public int refundedSecsAtk;
            public int energyMoveNet;
            public int energyAtkNet;
            public bool freeMoveApplied;
            public string refundTag;
            public bool attackExecuted;

            public int TotalPlanned => Mathf.Max(0, plannedSecsMove + plannedSecsAtk);
            public int TotalRefunded => Mathf.Max(0, refundedSecsMove + refundedSecsAtk);
            public int NetSeconds => Mathf.Max(0, TotalPlanned - TotalRefunded);
            public int TotalEnergyNet => energyMoveNet + energyAtkNet;
            public bool AttackExecuted => attackExecuted;
        }

        readonly Stack<PreDeduct> _planStack = new();
        readonly List<ChainOption> _chainBuffer = new();
        readonly List<ChainOption> _derivedBuffer = new();

        IActionRules ResolveRules()
        {
            return rulebook != null ? (IActionRules)rulebook : ActionRulebook.Default;
        }

        TargetSelectionCursor ChainCursor
        {
            get
            {
                if (_chainCursor == null)
                {
                    var highlighter = EnsureChainHighlighter();
                    if (highlighter != null)
                        _chainCursor = new TargetSelectionCursor(highlighter);
                }
                return _chainCursor;
            }
        }


        void Reset()
        {
            chainKeybinds = new List<ChainKeybind>
            {
                new ChainKeybind { id = "Reaction40", key = KeyCode.Alpha1 },
                new ChainKeybind { id = "Reaction20", key = KeyCode.Alpha2 },
                new ChainKeybind { id = "Free10", key = KeyCode.Alpha3 },
                new ChainKeybind { id = "FullRoundTest", key = KeyCode.Alpha4 },
                new ChainKeybind { id = "DerivedAfterAttack", key = KeyCode.Alpha5 },
                new ChainKeybind { id = "DerivedAfterDerived", key = KeyCode.Alpha6 }
            };
        }

        void Awake()
        {
            foreach (var mb in tools)
            {
                if (!mb) continue;
                WireTurnManager(mb);
                InjectCursorHighlighter(mb);
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

        void InjectCursorHighlighter(MonoBehaviour mb)
        {
            if (mb == null)
                return;

            switch (mb)
            {
                case AttackControllerV2 attack:
                    attack.SetCursorHighlighter(EnsureAimHighlighter());
                    break;
                case ChainTestActionBase chainTool:
                    chainTool.SetCursorHighlighter(EnsureChainHighlighter());
                    break;
            }
        }

        HexBoardTiler ResolveTiler()
        {
            if (!tiler && authoring != null)
                tiler = authoring.GetComponent<HexBoardTiler>() ?? authoring.GetComponentInParent<HexBoardTiler>(true);
            return tiler;
        }

        IHexHighlighter EnsureAimHighlighter()
        {
            if (_aimHighlighter == null)
            {
                var resolved = ResolveTiler();
                if (resolved != null)
                    _aimHighlighter = new HexAreaPainter(resolved);
            }
            return _aimHighlighter;
        }

        IHexHighlighter EnsureChainHighlighter()
        {
            if (_chainHighlighter == null)
            {
                var resolved = ResolveTiler();
                if (resolved != null)
                    _chainHighlighter = new HexAreaPainter(resolved);
            }
            return _chainHighlighter;
        }

        void WireMoveCostAdapter(MoveCostServiceV2Adapter adapter)
        {
            if (adapter == null) return;
            adapter.turnManager = turnManager;
        }

        void RegisterPhaseGate()
        {
            if (turnManager != null)
                turnManager.RegisterPhaseStartGate(HandlePhaseStartGate);
        }

        void UnregisterPhaseGate()
        {
            if (turnManager != null)
                turnManager.UnregisterPhaseStartGate(HandlePhaseStartGate);
        }

        void OnEnable()
        {
            if (turnManager != null)
            {
                turnManager.TurnStarted += OnTurnStarted;
            }
            if (registerAsGateHub)
            {
                RegisterPhaseGate();
                if (turnManager != null)
                {
                    turnManager.RegisterTurnStartGate(HandleTurnStartGate);
                }
            }
        }

        void OnDisable()
        {
            if (turnManager != null)
            {
                turnManager.TurnStarted -= OnTurnStarted;
            }
            if (registerAsGateHub)
                UnregisterPhaseGate();
            if (turnManager != null)
            {
                turnManager.UnregisterTurnStartGate(HandleTurnStartGate);
            }
            ChainCursor?.Clear();
        }

        void OnTurnStarted(Unit unit)
        {
            _currentUnit = unit;
            if (_activeTool != null && ResolveUnit(_activeTool) != _currentUnit)
                Cancel(false);

            if (!registerAsGateHub || turnManager == null || unit == null)
                return;
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

        void HandleIdleKeybind(KeyCode key, string toolId)
        {
            if (key == KeyCode.None || string.IsNullOrEmpty(toolId))
                return;

            if (Input.GetKeyDown(key))
                TryRequestIdleAction(toolId);
        }

        void TryRequestIdleAction(string toolId)
        {
            var tool = SelectTool(toolId);
            if (tool == null)
                return;

            if (!CanActivateAtIdle(tool))
                return;

            RequestAim(toolId);
        }

        bool CanActivateAtIdle(IActionToolV2 tool)
        {
            if (tool == null)
                return false;
            var owner = ResolveUnit(tool);

            var rules = ResolveRules();
            if (rules != null && !rules.CanActivateAtIdle(tool.Kind))
                return false;

            if (rules != null && !rules.AllowFriendlyInsertions())
            {
                if (_currentUnit != null && owner != _currentUnit)
                    return false;
            }
            if (turnManager != null && owner != null && turnManager.HasActiveFullRound(owner))
                return false;

            return true;
        }

        void Update()
        {
            if (_phase == Phase.Idle && !IsInputSuppressed)
            {
                HandleIdleKeybind(keyMoveAim, "Move");
                HandleIdleKeybind(keyAttackAim, "Attack");
                if (chainKeybinds != null)
                {
                    foreach (var bind in chainKeybinds)
                    {
                        if (bind.key == KeyCode.None || string.IsNullOrEmpty(bind.id))
                            continue;
                        if (Input.GetKeyDown(bind.key))
                            TryRequestIdleAction(bind.id);
                    }
                }
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
            if (tool is ChainTestActionBase chain && chain != null)
                return chain.ResolveUnit();
            return unitDriver != null ? unitDriver.UnitRef : null;
        }

        public void RequestAim(string toolId)
        {
            if (_phase != Phase.Idle) return;

            var tool = SelectTool(toolId);
            if (tool == null) return;
            if (_phase == Phase.Idle && !CanActivateAtIdle(tool))
                return;
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

        public bool TryAutoExecuteAction(string toolId, Hex target)
        {
            if (_phase != Phase.Idle)
                return false;

            var tool = SelectTool(toolId);
            if (tool == null)
                return false;

            if (!CanActivateAtIdle(tool))
                return false;

            if (IsExecuting || IsAnyToolBusy())
                return false;

            var unit = ResolveUnit(tool);
            if (_currentUnit != null && unit != _currentUnit)
                return false;

            if (!TryBeginAim(tool, unit, out var reason))
            {
                if (!string.IsNullOrEmpty(reason))
                    ActionPhaseLogger.Log(unit, tool.Id, "W1_AimReject", $"(reason={reason})");
                return false;
            }

            if (_activeTool != null)
                CleanupAfterAbort(_activeTool, false);

            _activeTool = tool;
            _hover = target;
            _activeTool.OnEnterAim();
            _phase = Phase.Aiming;
            ActionPhaseLogger.Log(unit, tool.Id, "W1_AimBegin");

            StartCoroutine(ConfirmRoutine(_activeTool, unit, target));
            return true;
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
            _planStack.Clear();
            _phase = Phase.Executing;
            string kind = tool.Id;
            ActionPhaseLogger.Log(unit, kind, "W2_ConfirmStart");

            if (!target.HasValue)
            {
                ActionPhaseLogger.Log(unit, kind, "W2_PrecheckOk");
                ActionPhaseLogger.Log(unit, kind, "W2_PreDeductCheckFail", "(reason=targetInvalid)");
                ActionPhaseLogger.Log(unit, kind, "W2_ConfirmAbort", "(reason=targetInvalid)");
                NotifyConfirmAbort(tool, unit, "targetInvalid");
                CleanupAfterAbort(tool, false);
                yield break;
            }

            if (!TryBeginAim(tool, unit, out var aimReason, false))
            {
                ActionPhaseLogger.Log(unit, kind, "W2_PrecheckOk");
                string fail = string.IsNullOrEmpty(aimReason) ? "notReady" : aimReason;
                ActionPhaseLogger.Log(unit, kind, "W2_PreDeductCheckFail", $"(reason={fail})");
                ActionPhaseLogger.Log(unit, kind, "W2_ConfirmAbort", $"(reason={fail})");
                NotifyConfirmAbort(tool, unit, fail);
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

            var budget = turnManager != null && unit != null ? turnManager.GetBudget(unit) : null;
            var resources = turnManager != null && unit != null ? turnManager.GetResources(unit) : null;
            var cooldowns = turnManager != null && unit != null ? turnManager.GetCooldowns(unit) : null;

            var cost = actionPlan.cost;
            if (tool.Kind == ActionKind.FullRound)
            {
                int remaining = budget != null ? Mathf.Max(0, budget.Remaining) : 0;
                if (remaining <= 0)
                {
                    ActionPhaseLogger.Log(unit, kind, "W2_PreDeductCheckFail", "(reason=lackTime)");
                    ActionPhaseLogger.Log(unit, kind, "W2_ConfirmAbort", "(reason=lackTime)");
                    NotifyConfirmAbort(tool, unit, "lackTime");
                    CleanupAfterAbort(tool, false);
                    yield break;
                }

                cost.moveSecs = remaining;
                cost.atkSecs = 0;
                actionPlan.cost = cost;
                cost = actionPlan.cost;

                if (tool is IFullRoundActionTool fullRoundTool)
                    fullRoundTool.PrepareFullRoundSeconds(cost.TotalSeconds);
            }

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
                NotifyConfirmAbort(tool, unit, failReason);
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

            var basePreDeduct = new PreDeduct
            {
                secs = cost.TotalSeconds,
                energyMove = cost.moveEnergy,
                energyAtk = cost.atkEnergy,
                valid = true
            };
            _planStack.Push(basePreDeduct);

            TryHideAllAimUI();

            var pendingChain = new List<Tuple<IActionToolV2, ActionPlan>>();
            bool cancelBase = false;
            if (ShouldOpenChainWindow(tool, unit))
            {
                bool isEnemyPhase = turnManager != null && !turnManager.IsPlayerPhase;
                yield return RunChainWindow(unit, actionPlan, tool.Kind, isEnemyPhase, budget, resources, cooldowns, cost.TotalSeconds, pendingChain, cancelled => cancelBase = cancelled);
            }

            for (int i = pendingChain.Count - 1; i >= 0; --i)
            {
                var pending = pendingChain[i];
                if (pending?.Item1 != null)
                    yield return ExecuteAndResolve(pending.Item1, unit, pending.Item2, budget, resources);
            }

            if (cancelBase)
            {
                if (_planStack.Count > 0)
                    _planStack.Pop();
                if (budget != null && basePreDeduct.valid && basePreDeduct.secs > 0)
                    budget.RefundTime(basePreDeduct.secs);
                ActionPhaseLogger.Log(unit, actionPlan.kind, "W2_ConfirmAbort", "(reason={LinkCancelled})");
                NotifyConfirmAbort(tool, unit, "LinkCancelled");
                CleanupAfterAbort(tool, false);
                yield break;
            }
            if (tool.Kind == ActionKind.FullRound && tool is IFullRoundActionTool frTool)
            {
                var planData = BuildFullRoundPlan(actionPlan, basePreDeduct, budget, resources);
                frTool.TriggerFullRoundImmediate(unit, turnManager, planData);

                if (budget != null)
                    planData.budgetAfter = budget.Remaining;
                if (resources != null)
                    planData.energyAfter = resources.Get("Energy");

                if (_planStack.Count > 0)
                    _planStack.Pop();

                ApplyCooldown(tool, unit);

                if (turnManager != null && unit != null)
                {
                    int rounds = Mathf.Max(1, frTool.FullRoundRounds);
                    turnManager.RegisterFullRound(unit, rounds, actionPlan.kind, frTool, planData);
                    turnManager.EndTurn(unit);
                }

                if (tool is IActionExecReportV2 execReport)
                    execReport.Consume();

                _activeTool = null;
                _hover = null;
                _phase = Phase.Idle;
                yield break;
            }
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

            yield return StartCoroutine(Resolve(tool, unit, plan, exec, report, budget, resources));
        }

        IEnumerator Resolve(IActionToolV2 tool, Unit unit, ActionPlan plan, IActionExecReportV2 exec, ExecReportData report, ITurnBudget budget, IResourcePool resources)
        {
            int used = report.TotalPlanned;
            int refunded = report.TotalRefunded;
            int net = report.NetSeconds;
            int energyMove = report.energyMoveNet;
            int energyAtk = report.energyAtkNet;
            int energyAction = report.TotalEnergyNet;
            bool freeMove = report.freeMoveApplied;
            string refundTag = report.refundTag;
            string reasonSuffix = string.IsNullOrEmpty(refundTag) ? string.Empty : $", refundReason={refundTag}";

            ActionPhaseLogger.Log(unit, plan.kind, "W4_ResolveBegin", $"(used={used}, refunded={refunded}, net={net}, energyMove={energyMove}, energyAtk={energyAtk}, energyAction={energyAction}{reasonSuffix})");

            if (tool is IActionResolveEffect resolveEffect)
            {
                try
                {
                    resolveEffect.OnResolve(unit, plan.target);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, this);
                }
            }

            PreDeduct preDeduct = _planStack.Count > 0 ? _planStack.Pop() : default;

            int plannedSecs = preDeduct.valid ? Mathf.Max(0, preDeduct.secs) : 0;
            if (budget != null && preDeduct.valid)
            {
                string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                string timeReason = !string.IsNullOrEmpty(refundTag) ? refundTag : (freeMove ? "FreeMove" : null);
                string timeSuffix = string.IsNullOrEmpty(timeReason) ? string.Empty : $" (reason={timeReason})";
                int delta = net - plannedSecs;
                if (delta < 0)
                {
                    int refundAmount = -delta;
                    if (refundAmount > 0)
                    {
                        budget.RefundTime(refundAmount);
                    }
                }
                else if (delta > 0)
                {
                    budget.SpendTime(delta);
                    Log($"[Time] Spend {unitLabel} {delta}s -> Remain={budget.Remaining}");
                }
            }

            if (resources != null)
            {
                string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                int plannedMoveEnergy = preDeduct.valid ? Mathf.Max(0, preDeduct.energyMove) : 0;
                int plannedAtkEnergy = preDeduct.valid ? Mathf.Max(0, preDeduct.energyAtk) : 0;

                int moveDelta = energyMove - plannedMoveEnergy;
                if (moveDelta > 0)
                {
                    resources.Spend("Energy", moveDelta, "Resolve_Move");
                }
                else if (moveDelta < 0)
                {
                    int refundAmount = -moveDelta;
                    string moveReason = !string.IsNullOrEmpty(refundTag)
                        ? refundTag
                        : (freeMove ? "FreeMove" : null);
                    bool moveSilent = string.IsNullOrEmpty(moveReason);
                    resources.Refund("Energy", refundAmount, moveSilent ? string.Empty : moveReason, moveSilent);
                }


                int atkDelta = energyAtk - plannedAtkEnergy;
                if (atkDelta > 0)
                {
                    resources.Spend("Energy", atkDelta, "Resolve_Attack");
                }
                else if (atkDelta < 0)
                {
                    int refundAmount = -atkDelta;
                    string atkReason = !string.IsNullOrEmpty(refundTag) ? refundTag : null;
                    bool atkSilent = string.IsNullOrEmpty(atkReason);
                    resources.Refund("Energy", refundAmount, atkSilent ? string.Empty : atkReason, atkSilent);
                }
            }

            var derivedQueue = new List<Tuple<IActionToolV2, ActionPlan>>();
            var cooldowns = (turnManager != null && unit != null) ? turnManager.GetCooldowns(unit) : null;
            if (ShouldRunDerivedWindow(unit, tool))
                yield return RunDerivedWindow(unit, tool, plan, report, budget, resources, cooldowns, derivedQueue);
            else
                derivedQueue.Clear();

            int budgetAfter = budget != null ? budget.Remaining : 0;
            int energyAfter = resources != null ? resources.Get("Energy") : 0;
            ActionPhaseLogger.Log(unit, plan.kind, "W4_ResolveEnd", $"(budgetAfter={budgetAfter}, energyAfter={energyAfter})");

            ApplyCooldown(tool, unit);

            exec.Consume();
            _activeTool = null;
            _hover = null;
            _phase = Phase.Idle;

            if (derivedQueue.Count > 0)
            {
                for (int i = 0; i < derivedQueue.Count; i++)
                {
                    var pending = derivedQueue[i];
                    if (pending?.Item1 != null)
                        yield return ExecuteAndResolve(pending.Item1, unit, pending.Item2, budget, resources);
                }
            }
        }
        FullRoundQueuedPlan BuildFullRoundPlan(ActionPlan plan, PreDeduct preDeduct, ITurnBudget budget, IResourcePool resources)
        {
            var queued = new FullRoundQueuedPlan
            {
                valid = preDeduct.valid,
                target = plan.target,
                plannedSeconds = Mathf.Max(0, preDeduct.secs),
                plannedMoveEnergy = Mathf.Max(0, preDeduct.energyMove),
                plannedAttackEnergy = Mathf.Max(0, preDeduct.energyAtk),
                budgetBefore = budget != null ? budget.Remaining : 0,
                energyBefore = resources != null ? resources.Get("Energy") : 0,
                budgetAfter = budget != null ? budget.Remaining : 0,
                energyAfter = resources != null ? resources.Get("Energy") : 0
            };
            return queued;
        }
        void ApplyCooldown(IActionToolV2 tool, Unit unit)
        {
            if (turnManager == null || tool == null || unit == null)
                return;

            var cooldowns = turnManager.GetCooldowns(unit);
            if (cooldowns == null)
                return;

            switch (tool)
            {
                case ChainTestActionBase chainTool:
                    int seconds = chainTool.CooldownSeconds;
                    string skillId = chainTool.CooldownId;
                    if (!string.IsNullOrEmpty(skillId) && seconds > 0)
                        cooldowns.StartSeconds(skillId, seconds);
                    break;
            }
        }

        ExecReportData BuildExecReport(IActionToolV2 tool, out IActionExecReportV2 exec)
        {
            exec = tool as IActionExecReportV2;
            if (exec == null)
                return default;

            var data = new ExecReportData
            {
                valid = false,
                plannedSecsMove = 0,
                plannedSecsAtk = 0,
                refundedSecsMove = 0,
                refundedSecsAtk = 0,
                energyMoveNet = 0,
                energyAtkNet = 0,
                freeMoveApplied = false,
                refundTag = null
            };

            if (tool is HexClickMover mover)
            {
                if (mover.HasPendingExecReport)
                {
                    data.valid = true;
                    data.plannedSecsMove = Mathf.Max(0, mover.ReportUsedSeconds);
                    data.refundedSecsMove = Mathf.Max(0, mover.ReportRefundedSeconds);
                    data.energyMoveNet = mover.ReportEnergyMoveNet;
                    data.energyAtkNet = 0;
                    data.freeMoveApplied = mover.ReportFreeMoveApplied;
                    data.refundTag = mover.ReportRefundTag;
                    data.attackExecuted = true;
                }
            }
            else if (tool is AttackControllerV2 attack)
            {
                if (attack.HasPendingExecReport)
                {
                    data.valid = true;
                    data.plannedSecsMove = Mathf.Max(0, attack.ReportMoveUsedSeconds);
                    data.plannedSecsAtk = Mathf.Max(0, attack.ReportAttackUsedSeconds);
                    data.refundedSecsMove = Mathf.Max(0, attack.ReportMoveRefundSeconds);
                    data.refundedSecsAtk = Mathf.Max(0, attack.ReportAttackRefundSeconds);
                    data.energyMoveNet = attack.ReportEnergyMoveNet;
                    data.energyAtkNet = attack.ReportEnergyAtkNet;
                    data.freeMoveApplied = attack.ReportFreeMoveApplied;
                    data.refundTag = attack.ReportRefundTag;
                    data.attackExecuted = attack.ReportAttackExecuted;
                }
            }
            else
            {
                data.valid = true;
                data.plannedSecsMove = Mathf.Max(0, exec.UsedSeconds);
                data.refundedSecsMove = Mathf.Max(0, exec.RefundedSeconds);
                if (exec is IActionEnergyReportV2 energyReport)
                {
                    data.energyMoveNet = energyReport.EnergyUsed;
                }
                data.attackExecuted = true;
            }

            return data;
        }

        void LogExecSummary(Unit unit, string kind, ExecReportData report)
        {
            string label = TurnManagerV2.FormatUnitLabel(unit);
            string freeMove = report.freeMoveApplied ? " (FreeMove)" : string.Empty;
            string reason = string.IsNullOrEmpty(report.refundTag) ? string.Empty : $" [{report.refundTag}]";
            if (string.Equals(kind, "Move", System.StringComparison.OrdinalIgnoreCase))
            {
                Log($"[Move] Use secs={report.TotalPlanned}s refund={report.TotalRefunded}s energy={report.energyMoveNet} U={label}{freeMove}{reason}");
            }
            else if (string.Equals(kind, "Attack", System.StringComparison.OrdinalIgnoreCase))
            {
                Log($"[Attack] Use moveSecs={report.plannedSecsMove}s atkSecs={report.plannedSecsAtk}s energyMove={report.energyMoveNet} energyAtk={report.energyAtkNet} U={label}{freeMove}{reason}");
            }
            else
            {
                Log($"[Action] {label} [{kind}] ExecSummary used={report.TotalPlanned}s refund={report.TotalRefunded}s energy={report.energyMoveNet + report.energyAtkNet}{freeMove}{reason}");
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
            _planStack.Clear();
        }

        void NotifyConfirmAbort(IActionToolV2 tool, Unit unit, string reason)
        {
            if (tool == null)
                return;

            if (string.IsNullOrEmpty(reason))
                reason = "notReady";

            switch (tool)
            {
                case HexClickMover mover:
                    mover.HandleConfirmAbort(unit, reason);
                    break;
                case AttackControllerV2 attack:
                    attack.HandleConfirmAbort(unit, reason);
                    break;
            }
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
                if (tool.Kind == ActionKind.FullRound && !turnManager.CanDeclareFullRound(unit, out var frReason))
                {
                    reason = string.IsNullOrEmpty(frReason) ? "fullRoundBlock" : frReason;
                    return false;
                }
            }

            return true;
        }

        PlannedCost GetBaselineCost(IActionToolV2 tool)
        {
            if (tool is IActionCostPreviewV2 preview && preview.TryPeekCost(out var previewSecs, out var previewEnergy))
            {
                return new PlannedCost
                {
                    moveSecs = Mathf.Max(0, previewSecs),
                    moveEnergy = Mathf.Max(0, previewEnergy),
                    atkSecs = 0,
                    atkEnergy = 0,
                    valid = true
                };
            }

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
            if (tool is IActionCostPreviewV2 preview && preview.TryPeekCost(out var previewSecs, out var previewEnergy))
            {
                return new PlannedCost
                {
                    moveSecs = Mathf.Max(0, previewSecs),
                    moveEnergy = Mathf.Max(0, previewEnergy),
                    atkSecs = 0,
                    atkEnergy = 0,
                    valid = true
                };
            }

            if (tool is HexClickMover mover)
            {
                var planned = mover.PeekPlannedCost(target);
                return new PlannedCost
                {
                    moveSecs = Mathf.Max(0, planned.moveSecs),
                    atkSecs = 0,
                    moveEnergy = Mathf.Max(0, planned.moveEnergy),
                    atkEnergy = 0,
                    valid = planned.valid
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

        bool ShouldOpenChainWindow(IActionToolV2 tool, Unit unit)
        {
            if (tool == null || unit == null || turnManager == null)
                return false;
            if (!turnManager.IsPlayerUnit(unit))
                return false;
            if (!turnManager.IsPlayerPhase)
                return false;

            var rules = ResolveRules();
            if (rules == null)
                return false;

            bool isEnemyPhase = turnManager != null && !turnManager.IsPlayerPhase;
            var allowed = rules.AllowedChainFirstLayer(tool.Kind, isEnemyPhase);
            return allowed != null && allowed.Count > 0;
        }

        static bool IsDerivedSourceKind(ActionKind kind)
        {
            return kind == ActionKind.Standard
                || kind == ActionKind.Reaction
                || kind == ActionKind.Derived;
        }

        bool ShouldRunDerivedWindow(Unit unit, IActionToolV2 baseTool)
        {
            if (unit == null || baseTool == null)
                return false;
            if (turnManager == null)
                return false;
            if (!turnManager.IsPlayerPhase)
                return false;
            if (!turnManager.IsPlayerUnit(unit))
                return false;

            return IsDerivedSourceKind(baseTool.Kind);
        }

        KeyCode ResolveChainKey(string id)
        {
            if (string.IsNullOrEmpty(id))
                return KeyCode.None;

            foreach (var bind in chainKeybinds)
            {
                if (!string.IsNullOrEmpty(bind.id) && string.Equals(bind.id, id, StringComparison.OrdinalIgnoreCase))
                    return bind.key;
            }

            return KeyCode.None;
        }

        List<ChainOption> BuildChainOptions(Unit unit, ITurnBudget budget, IResourcePool resources, int baseTimeCost, IReadOnlyList<ActionKind> allowedKinds, ICooldownSink cooldowns, ISet<IActionToolV2> pending)
        {
            _chainBuffer.Clear();
            if (allowedKinds == null || allowedKinds.Count == 0)
                return _chainBuffer;

            var rules = ResolveRules();
            bool enforceReactionWithinBase = rules?.ReactionMustBeWithinBaseTime() ?? true;
            bool allowFriendlyInsertion = rules?.AllowFriendlyInsertions() ?? false;

            foreach (var pair in _toolsById)
            {
                foreach (var tool in pair.Value)
                {
                    if (tool == null)
                        continue;

                    var owner = ResolveUnit(tool);
                    if (!allowFriendlyInsertion && owner != unit)
                        continue;
                    if (allowFriendlyInsertion && owner == null)
                        continue;
                    if (turnManager != null && owner != null && turnManager.HasActiveFullRound(owner))
                    {
                        Log($"[FullRound] ChainWindow skip owner={TurnManagerV2.FormatUnitLabel(owner)} id={tool.Id} reason=fullround");
                        continue;
                    }

                    if (pending != null && pending.Contains(tool))
                        continue;

                    bool kindAllowed = false;
                    for (int i = 0; i < allowedKinds.Count; i++)
                    {
                        if (allowedKinds[i] == tool.Kind)
                        {
                            kindAllowed = true;
                            break;
                        }
                    }

                    if (!kindAllowed)
                        continue;

                    if (tool.Kind == ActionKind.Derived)
                        continue;

                    var cost = GetBaselineCost(tool);
                    if (!cost.valid)
                        continue;

                    int secs = cost.TotalSeconds;
                    int energy = cost.TotalEnergy;

                    if (tool.Kind == ActionKind.Reaction)
                    {
                        if (enforceReactionWithinBase)
                        {
                            if (baseTimeCost <= 0 && secs > 0)
                                continue;
                            if (secs > baseTimeCost)
                                continue;
                        }
                    }

                    if (tool.Kind == ActionKind.Free && secs != 0)
                        continue;

                    if (secs > 0 && budget != null && !budget.HasTime(secs))
                        continue;

                    if (resources != null && energy > 0 && !resources.Has("Energy", energy))
                        continue;

                    if (cooldowns != null && !IsCooldownReadyForConfirm(tool, cooldowns))
                        continue;

                    var key = ResolveChainKey(tool.Id);
                    if (key == KeyCode.None)
                        continue;

                    _chainBuffer.Add(new ChainOption
                    {
                        tool = tool,
                        key = key,
                        secs = secs,
                        energy = energy,
                        kind = tool.Kind
                    });
                }
            }

            return _chainBuffer;
        }

        static string FormatChainStageLabel(int depth)
        {
            if (depth <= 0)
                return "W2.1";
            return $"W2.1.{depth}";
        }

        IEnumerator RunChainWindow(Unit unit, ActionPlan basePlan, ActionKind baseKind, bool isEnemyPhase, ITurnBudget budget, IResourcePool resources, ICooldownSink cooldowns, int baseTimeCost, List<Tuple<IActionToolV2, ActionPlan>> pendingActions, Action<bool> onComplete)
        {
            PushInputSuppression();
            try
            {
                var rules = ResolveRules();
                IReadOnlyList<ActionKind> allowedKinds = rules?.AllowedChainFirstLayer(baseKind, isEnemyPhase);
                bool cancelledBase = false;
                int depth = 0;
                bool keepLooping = allowedKinds != null && allowedKinds.Count > 0;
                HashSet<IActionToolV2> pendingSet = null;
                if (pendingActions != null && pendingActions.Count > 0)
                {
                    pendingSet = new HashSet<IActionToolV2>();
                    foreach (var tuple in pendingActions)
                    {
                        if (tuple?.Item1 != null)
                            pendingSet.Add(tuple.Item1);
                    }
                }

                if (!keepLooping)
                {
                    string initialLabel = FormatChainStageLabel(depth);
                    ActionPhaseLogger.Log(unit, basePlan.kind, initialLabel, "(count:0)");
                    ActionPhaseLogger.Log(unit, basePlan.kind, $"{initialLabel} Skip");
                    onComplete?.Invoke(false);
                    yield break;
                }

                while (keepLooping)
                {
                    var options = BuildChainOptions(unit, budget, resources, baseTimeCost, allowedKinds, cooldowns, pendingSet);
                    string label = FormatChainStageLabel(depth);
                    ActionPhaseLogger.Log(unit, basePlan.kind, label, $"(count:{options.Count})");

                    if (options.Count == 0)
                    {
                        ActionPhaseLogger.Log(unit, basePlan.kind, $"{label} Skip");
                        break;
                    }

                    bool resolvedStage = false;
                    while (!resolvedStage)
                    {
                        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                        {
                            ActionPhaseLogger.Log(unit, basePlan.kind, $"{label} Cancel");
                            resolvedStage = true;
                            keepLooping = false;
                            break;
                        }

                        foreach (var option in options)
                        {
                            if (option.key != KeyCode.None && Input.GetKeyDown(option.key))
                            {
                                ActionPhaseLogger.Log(unit, basePlan.kind, $"{label} Select", $"(id={option.tool.Id}, kind={option.kind}, secs={option.secs}, energy={option.energy})");

                                ChainQueueOutcome outcome = default;
                                yield return TryQueueChainSelection(option, unit, basePlan.kind, label, budget, resources, pendingActions, result => outcome = result);

                                if (outcome.cancel)
                                {
                                    ActionPhaseLogger.Log(unit, basePlan.kind, $"{label} Cancel");
                                    keepLooping = false;
                                    resolvedStage = true;
                                    break;
                                }

                                if (!outcome.queued)
                                    continue;

                                if (outcome.tool != null)
                                {
                                    pendingSet ??= new HashSet<IActionToolV2>();
                                    pendingSet.Add(outcome.tool);
                                }

                                if (option.kind == ActionKind.Reaction && turnManager != null && turnManager.IsPlayerPhase && turnManager.IsPlayerUnit(unit))
                                {
                                    cancelledBase = true;
                                }

                                if (rules != null)
                                    allowedKinds = rules.AllowedChainNextLayer(option.kind);
                                else
                                    allowedKinds = Array.Empty<ActionKind>();

                                keepLooping = allowedKinds != null && allowedKinds.Count > 0;

                                depth += 1;
                                resolvedStage = true;
                                break;
                            }
                        }

                        if (!resolvedStage)
                            yield return null;
                    }
                }
                onComplete?.Invoke(cancelledBase);
            }
            finally
            {
                PopInputSuppression();
            }
        }

        List<ChainOption> BuildDerivedOptions(Unit unit, ITurnBudget budget, IResourcePool resources, ICooldownSink cooldowns, IReadOnlyList<string> allowedIds)
        {
            _derivedBuffer.Clear();
            if (allowedIds == null || allowedIds.Count == 0)
                return _derivedBuffer;

            for (int i = 0; i < allowedIds.Count; i++)
            {
                string id = allowedIds[i];
                if (string.IsNullOrEmpty(id))
                    continue;

                if (!_toolsById.TryGetValue(id, out var toolsForId))
                    continue;

                for (int j = 0; j < toolsForId.Count; j++)
                {
                    var tool = toolsForId[j];
                    if (tool == null)
                        continue;

                    if (tool.Kind != ActionKind.Derived)
                        continue;

                    var owner = ResolveUnit(tool);
                    if (owner != unit)
                        continue;

                    var cost = GetBaselineCost(tool);
                    if (!cost.valid)
                        continue;

                    int secs = cost.TotalSeconds;
                    int energy = cost.TotalEnergy;

                    if (secs > 0 && budget != null && !budget.HasTime(secs))
                        continue;

                    if (resources != null && energy > 0 && !resources.Has("Energy", energy))
                        continue;

                    if (cooldowns != null && !IsCooldownReadyForConfirm(tool, cooldowns))
                        continue;

                    var key = ResolveChainKey(tool.Id);
                    if (key == KeyCode.None)
                        continue;

                    _derivedBuffer.Add(new ChainOption
                    {
                        tool = tool,
                        key = key,
                        secs = secs,
                        energy = energy,
                        kind = tool.Kind
                    });
                }
            }

            return _derivedBuffer;
        }

        bool DetermineDerivedBaseSuccess(IActionToolV2 tool, ExecReportData report)
        {
            if (!report.valid)
                return false;

            if (tool == null)
                return true;

            if (tool is AttackControllerV2)
                return report.AttackExecuted;

            return true;
        }

        IEnumerator RunDerivedWindow(Unit unit, IActionToolV2 baseTool, ActionPlan basePlan, ExecReportData report, ITurnBudget budget, IResourcePool resources, ICooldownSink cooldowns, List<Tuple<IActionToolV2, ActionPlan>> derivedQueue)
        {
            PushInputSuppression();
            try
            {
                derivedQueue ??= new List<Tuple<IActionToolV2, ActionPlan>>();

                var rules = ResolveRules();
                var allowedIds = rules?.AllowedDerivedActions(basePlan.kind);
                bool baseSuccess = DetermineDerivedBaseSuccess(baseTool, report);

                if (!baseSuccess)
                {
                    Log($"[Chain] DerivedPromptOpen(from={basePlan.kind}, count=0, baseSuccess=false)");
                    Log("[Chain] DerivedPromptAbort(base-fail)");
                    _derivedBuffer.Clear();
                    yield break;
                }

                if (allowedIds == null || allowedIds.Count == 0)
                {
                    Log($"[Chain] DerivedPromptOpen(from={basePlan.kind}, count=0, baseSuccess=true)");
                    Log("[Chain] DerivedPromptAbort(auto-skip)");
                    _derivedBuffer.Clear();
                    yield break;
                }

                var options = BuildDerivedOptions(unit, budget, resources, cooldowns, allowedIds);
                Log($"[Chain] DerivedPromptOpen(from={basePlan.kind}, count={options.Count}, baseSuccess=true)");

                if (options.Count == 0)
                {
                    Log("[Chain] DerivedPromptAbort(auto-skip)");
                    options.Clear();
                    yield break;
                }

                bool resolved = false;
                while (!resolved)
                {
                    if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                    {
                        Log("[Chain] DerivedPromptAbort(cancel)");
                        break;
                    }

                    bool handled = false;
                    for (int i = 0; i < options.Count; i++)
                    {
                        var option = options[i];
                        if (option.key != KeyCode.None && Input.GetKeyDown(option.key))
                        {
                            handled = true;
                            Log($"[Derived] Select(id={option.tool.Id}, kind={option.kind})");
                            ChainQueueOutcome outcome = default;
                            yield return TryQueueDerivedSelection(option, unit, basePlan.kind, budget, resources, derivedQueue, result => outcome = result);

                            if (outcome.cancel)
                            {
                                Log("[Chain] DerivedPromptAbort(cancel)");
                                resolved = true;
                            }
                            else if (outcome.queued)
                            {
                                resolved = true;
                            }
                            break;
                        }
                    }

                    if (resolved)
                        break;

                    if (!handled)
                        yield return null;
                    else
                        yield return null;
                }

                options.Clear();
            }
            finally
            {
                PopInputSuppression();
            }
        }

        IEnumerator TryQueueDerivedSelection(ChainOption option, Unit unit, string baseId, ITurnBudget budget, IResourcePool resources, List<Tuple<IActionToolV2, ActionPlan>> derivedQueue, Action<ChainQueueOutcome> onComplete)
        {
            var tool = option.tool;
            if (tool == null)
            {
                onComplete?.Invoke(default);
                yield break;
            }

            Hex selectedTarget = Hex.Zero;
            bool targetChosen = true;

            tool.OnEnterAim();
            ActionPhaseLogger.Log(unit, tool.Id, "W1_AimBegin");

            if (tool is ChainTestActionBase chainTool)
            {
                bool awaitingSelection = true;
                targetChosen = false;
                var cursor = ChainCursor;
                cursor?.Clear();
                Hex? lastHover = null;

                while (awaitingSelection)
                {
                    var hover = PickHexUnderMouse();
                    if (hover.HasValue)
                    {
                        if (!lastHover.HasValue || !lastHover.Value.Equals(hover.Value))
                        {
                            chainTool.OnHover(hover.Value);
                            lastHover = hover.Value;
                        }
                        var check = chainTool.ValidateTarget(unit, hover.Value);

                        if (Input.GetMouseButtonDown(0))
                        {
                            if (check.ok)
                            {
                                selectedTarget = hover.Value;
                                ActionPhaseLogger.Log(unit, baseId, "W4.5 TargetOk", $"(id={tool.Id}, hex={hover.Value})");
                                targetChosen = true;
                                awaitingSelection = false;
                            }
                            else
                            {
                                ActionPhaseLogger.Log(unit, baseId, "W4.5 TargetInvalid", $"(id={tool.Id}, reason={check.reason})");
                            }
                        }
                    }
                    else
                    {
                        cursor?.Clear();
                        lastHover = null;
                    }

                    if (!awaitingSelection)
                        break;

                    if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
                    {
                        cursor?.Clear();
                        ActionPhaseLogger.Log(unit, tool.Id, "W1_AimCancel");
                        tool.OnExitAim();
                        onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                        yield break;
                    }

                    yield return null;
                }

                cursor?.Clear();

                if (!targetChosen)
                {
                    tool.OnExitAim();
                    onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                    yield break;
                }
            }

            tool.OnExitAim();
            ChainCursor?.Clear();

            ActionPhaseLogger.Log(unit, tool.Id, "W2_ConfirmStart");
            ActionPhaseLogger.Log(unit, tool.Id, "W2_PrecheckOk");

            if (!targetChosen)
            {
                onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                yield break;
            }

            string failReason = null;
            if (budget != null && option.secs > 0 && !budget.HasTime(option.secs))
                failReason = "lackTime";
            else if (resources != null && option.energy > 0 && !resources.Has("Energy", option.energy))
                failReason = "lackEnergy";

            if (failReason != null)
            {
                ActionPhaseLogger.Log(unit, tool.Id, "W2_PreDeductCheckFail", $"(reason={failReason})");
                ActionPhaseLogger.Log(unit, tool.Id, "W2_ConfirmAbort", $"(reason={failReason})");
                onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                yield break;
            }

            ActionPhaseLogger.Log(unit, tool.Id, "W2_PreDeductCheckOk");

            int timeBefore = budget != null ? budget.Remaining : 0;
            int energyBefore = resources != null ? resources.Get("Energy") : 0;
            Log($"[Gate] W2' PreDeduct planSecs={option.secs}, planEnergy={option.energy} before=Time:{timeBefore}/Energy:{energyBefore}");

            if (budget != null && option.secs > 0)
                budget.SpendTime(option.secs);

            if (resources != null && option.energy > 0)
                resources.Spend("Energy", option.energy, "PreDeduct_Derived");

            var plan = new PreDeduct
            {
                secs = option.secs,
                energyMove = option.energy,
                energyAtk = 0,
                valid = true
            };

            _planStack.Push(plan);

            var planCost = new PlannedCost
            {
                moveSecs = option.secs,
                atkSecs = 0,
                moveEnergy = option.energy,
                atkEnergy = 0,
                valid = true
            };

            var actionPlan = new ActionPlan
            {
                kind = tool.Id,
                target = selectedTarget,
                cost = planCost
            };

            derivedQueue.Add(Tuple.Create(tool, actionPlan));

            ChainCursor?.Clear();

            onComplete?.Invoke(new ChainQueueOutcome { queued = true, cancel = false, tool = option.tool });
        }

        IEnumerator TryQueueChainSelection(ChainOption option, Unit unit, string baseKind, string stageLabel, ITurnBudget budget, IResourcePool resources, List<Tuple<IActionToolV2, ActionPlan>> pendingActions, Action<ChainQueueOutcome> onComplete)
        {
            var tool = option.tool;
            if (tool == null)
            {
                onComplete?.Invoke(default);
                yield break;
            }

            Hex selectedTarget = Hex.Zero;
            bool targetChosen = true;

            tool.OnEnterAim();
            ActionPhaseLogger.Log(unit, tool.Id, "W1_AimBegin");

            if (tool is ChainTestActionBase chainTool)
            {
                bool awaitingSelection = true;
                targetChosen = false;
                var cursor = ChainCursor;
                cursor?.Clear();
                Hex? lastHover = null;

                while (awaitingSelection)
                {
                    var hover = PickHexUnderMouse();
                    if (hover.HasValue)
                    {
                        if (!lastHover.HasValue || !lastHover.Value.Equals(hover.Value))
                        {
                            chainTool.OnHover(hover.Value);
                            lastHover = hover.Value;
                        }
                        var check = chainTool.ValidateTarget(unit, hover.Value);

                        if (Input.GetMouseButtonDown(0))
                        {
                            if (check.ok)
                            {
                                selectedTarget = hover.Value;
                                ActionPhaseLogger.Log(unit, baseKind, $"{stageLabel} TargetOk", $"(id={tool.Id}, hex={hover.Value})");
                                targetChosen = true;
                                awaitingSelection = false;
                            }
                            else
                            {
                                ActionPhaseLogger.Log(unit, baseKind, $"{stageLabel} TargetInvalid", $"(id={tool.Id}, reason={check.reason})");
                            }
                        }
                    }
                    else
                    {
                        cursor?.Clear();
                        lastHover = null;
                    }

                    if (!awaitingSelection)
                        break;

                    if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
                    {
                        cursor?.Clear();
                        ActionPhaseLogger.Log(unit, tool.Id, "W1_AimCancel");
                        tool.OnExitAim();
                        onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                        yield break;
                    }

                    yield return null;
                }

                cursor?.Clear();

                if (!targetChosen)
                {
                    tool.OnExitAim();
                    onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                    yield break;
                }
            }

            tool.OnExitAim();
            ChainCursor?.Clear();

            ActionPhaseLogger.Log(unit, tool.Id, "W2_ConfirmStart");
            ActionPhaseLogger.Log(unit, tool.Id, "W2_PrecheckOk");

            if (!targetChosen)
            {
                onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                yield break;
            }

            string failReason = null;
            if (budget != null && option.secs > 0 && !budget.HasTime(option.secs))
                failReason = "lackTime";
            else if (resources != null && option.energy > 0 && !resources.Has("Energy", option.energy))
                failReason = "lackEnergy";

            if (failReason != null)
            {
                ActionPhaseLogger.Log(unit, tool.Id, "W2_PreDeductCheckFail", $"(reason={failReason})");
                ActionPhaseLogger.Log(unit, tool.Id, "W2_ConfirmAbort", $"(reason={failReason})");
                onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                yield break;
            }

            ActionPhaseLogger.Log(unit, tool.Id, "W2_PreDeductCheckOk");

            if (budget != null && option.secs > 0)
                budget.SpendTime(option.secs);

            if (resources != null && option.energy > 0)
            {
                string reason = $"PreDeduct_{option.kind}";
                resources.Spend("Energy", option.energy, reason);
            }

            var plan = new PreDeduct
            {
                secs = option.secs,
                energyMove = option.energy,
                energyAtk = 0,
                valid = true
            };

            _planStack.Push(plan);

            var planCost = new PlannedCost
            {
                moveSecs = option.secs,
                atkSecs = 0,
                moveEnergy = option.energy,
                atkEnergy = 0,
                valid = true
            };

            var actionPlan = new ActionPlan
            {
                kind = tool.Id,
                target = selectedTarget,
                cost = planCost
            };

            pendingActions.Add(Tuple.Create(tool, actionPlan));

            ChainCursor?.Clear();

            onComplete?.Invoke(new ChainQueueOutcome { queued = true, cancel = false, tool = option.tool });
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
            if (raw.Contains("fullround"))
                return "fullRound";
            if (raw.Contains("prepaid"))
                return "prepaid";
            if (raw.Contains("timespent"))
                return "timeSpent";
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
            else if (tool is ChainTestActionBase chain)
                skillId = chain.CooldownId;

            if (string.IsNullOrEmpty(skillId))
                skillId = tool?.Id;

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

        List<Unit> BuildOrderedSideUnits(bool isPlayerSide)
        {
            if (turnManager == null)
                return null;

            var source = turnManager.GetSideUnits(isPlayerSide);
            if (source == null || source.Count == 0)
                return null;

            var ordered = new List<Unit>(source.Count);
            foreach (var unit in source)
            {
                if (unit == null)
                    continue;
                ordered.Add(unit);
            }

            if (ordered.Count <= 1)
                return ordered;

            ordered.Sort((a, b) =>
            {
                int ia = turnManager.GetTurnOrderIndex(a, isPlayerSide);
                int ib = turnManager.GetTurnOrderIndex(b, isPlayerSide);

                if (ia == ib)
                {
                    string la = TurnManagerV2.FormatUnitLabel(a);
                    string lb = TurnManagerV2.FormatUnitLabel(b);
                    return string.Compare(la, lb, StringComparison.Ordinal);
                }

                if (ia == int.MaxValue)
                    return 1;
                if (ib == int.MaxValue)
                    return -1;

                return ia.CompareTo(ib);
            });

            return ordered;
        }

        int PreviewStartFreeChainOptions(Unit unit, ITurnBudget budget, IResourcePool resources)
        {
            if (unit == null)
                return 0;

            var cooldowns = turnManager != null ? turnManager.GetCooldowns(unit) : null;
            var rules = ResolveRules();
            var allowed = rules?.AllowedAtPhaseStartFree();
            if (allowed == null || allowed.Count == 0)
                return 0;

            var options = BuildChainOptions(unit, budget, resources, 0, allowed, cooldowns, null);
            int count = options != null ? options.Count : 0;
            options?.Clear();
            return count;
        }

        void LogPhaseStartPreview(string unitLabel, string phaseKind, int count)
        {
            Log($"[Free] {phaseKind} W2.1 (count={count}) unit={unitLabel}");
        }

        IEnumerator HandlePhaseStartGate(bool isPlayerPhase)
        {
            if (turnManager == null)
                yield break;

            if (isPlayerPhase)
            {
                // Player units handle their own free windows on TurnStart.
                yield break;
            }

            if (skipPhaseStartFreeChain)
            {
                var friendlies = BuildOrderedSideUnits(true);
                if (friendlies != null)
                {
                    foreach (var unit in friendlies)
                    {
                        if (unit == null)
                            continue;
                        Log($"[Free] PhaseStart(Enemy) freeskip unit={TurnManagerV2.FormatUnitLabel(unit)}");
                    }
                }
            }
        }

        IEnumerator HandleTurnStartGate(Unit unit)
        {
            if (turnManager == null || unit == null)
                yield break;

            bool isPlayerUnit = turnManager.IsPlayerUnit(unit);
            bool isEnemyUnit = turnManager.IsEnemyUnit(unit);

            if (!isPlayerUnit && !isEnemyUnit)
                yield break;

            if (isPlayerUnit)
            {
                if (unitDriver != null && unitDriver.UnitRef != unit)
                    yield break;

                if (skipPhaseStartFreeChain)
                {
                    Log($"[Free] PhaseStart(P1) freeskip unit={TurnManagerV2.FormatUnitLabel(unit)}");
                    yield break;
                }

                yield return RunStartFreeChainWindow(unit, "PhaseStart(P1)", false);
                yield break;
            }

            if (unitDriver != null)
            {
                var driverUnit = unitDriver.UnitRef;
                if (driverUnit != null && turnManager.IsEnemyUnit(driverUnit))
                    yield break;
            }

            if (skipPhaseStartFreeChain)
            {
                var friendlies = BuildOrderedSideUnits(true);
                if (friendlies != null)
                {
                    foreach (var friendly in friendlies)
                    {
                        if (friendly == null)
                            continue;
                        Log($"[Free] PhaseStart(Enemy) freeskip unit={TurnManagerV2.FormatUnitLabel(friendly)}");
                    }
                }
                yield break;
            }

            var orderedFriendlies = BuildOrderedSideUnits(true);
            if (orderedFriendlies == null || orderedFriendlies.Count == 0)
                yield break;

            foreach (var friendly in orderedFriendlies)
            {
                if (friendly == null)
                    continue;
                yield return RunStartFreeChainWindow(friendly, "PhaseStart(Enemy)", true);
            }
        }

        IEnumerator RunStartFreeChainWindow(Unit unit, string planKind, bool isEnemyPhase)
        {
            if (unit == null || turnManager == null)
                yield break;

            var budget = turnManager.GetBudget(unit);
            var resources = turnManager.GetResources(unit);

            _planStack.Clear();

            if (turnManager.HasActiveFullRound(unit))
            {
                Log($"[FullRound] {planKind} skip unit={TurnManagerV2.FormatUnitLabel(unit)} reason=fullround");
                yield break;
            }

            var pendingChain = new List<Tuple<IActionToolV2, ActionPlan>>();
            bool cancelBase = false;

            var phasePlan = new ActionPlan
            {
                kind = planKind,
                target = Hex.Zero,
                cost = new PlannedCost { valid = true }
            };

            // 可选：为了避免刚开回合时各句柄/工具还没就绪导致第一次统计为0，等1帧再开窗
            // yield return null;

            var cooldowns = turnManager.GetCooldowns(unit);

            yield return RunChainWindow(unit, phasePlan, ActionKind.Free, isEnemyPhase,
                budget, resources, cooldowns, 0, pendingChain, cancelled => cancelBase = cancelled);

            if (cancelBase)
            {
                _planStack.Clear();
                yield return null;
                yield break;
            }

            for (int i = pendingChain.Count - 1; i >= 0; --i)
            {
                var pending = pendingChain[i];
                if (pending?.Item1 != null)
                    yield return ExecuteAndResolve(pending.Item1, unit, pending.Item2, budget, resources);
            }

            _planStack.Clear();
            yield return null;
        }

    }
}