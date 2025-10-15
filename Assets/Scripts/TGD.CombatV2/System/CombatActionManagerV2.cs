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

        public enum PhaseState
        {
            Idle,
            Aiming,
            Executing
        }

        public PhaseState phase = PhaseState.Idle;
        public bool IsExecuting => phase == PhaseState.Executing;

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
            if (phase == PhaseState.Idle)
            {
                if (Input.GetKeyDown(keyMoveAim)) RequestAim("Move");
                if (Input.GetKeyDown(keyAttackAim)) RequestAim("Attack");
            }

            if (phase == PhaseState.Aiming)
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
            if (phase != PhaseState.Idle) return;

            var tool = SelectTool(toolId);
            if (tool == null) return;
            if (IsExecuting || IsAnyToolBusy()) return;

            var unit = ResolveUnit(tool);
            if (_currentUnit != null && unit != _currentUnit)
                return;

            if (!TryBeginAim(tool, unit, out var reason))
            {
                if (!string.IsNullOrEmpty(reason))
                    ActionPhaseLogger.Log(unit, tool.Id, "W1_AimDenied", $"(reason={reason})");
                return;
            }

            if (_activeTool != null)
                CleanupAfterAbort(_activeTool, false);

            _activeTool = tool;
            _hover = null;
            _activeTool.OnEnterAim();
            phase = PhaseState.Aiming;
            ActionPhaseLogger.Log(unit, tool.Id, "W1_AimBegin");
        }

        public void Cancel(bool userInitiated = false)
        {
            if (phase != PhaseState.Aiming || _activeTool == null)
                return;

            var unit = ResolveUnit(_activeTool);
            if (userInitiated)
                ActionPhaseLogger.Log(unit, _activeTool.Id, "W1_AimCancel");

            _activeTool.OnExitAim();
            _activeTool = null;
            _hover = null;
            phase = PhaseState.Idle;
        }

        public void Confirm()
        {
            if (phase != PhaseState.Aiming || _activeTool == null)
                return;

            var unit = ResolveUnit(_activeTool);
            var hex = _hover ?? PickHexUnderMouse();

            StartCoroutine(ConfirmRoutine(_activeTool, unit, hex));
        }

        IEnumerator ConfirmRoutine(IActionToolV2 tool, Unit unit, Hex? target)
        {
            ActionPhaseLogger.Log(unit, tool.Id, "W2_ConfirmStart");

            if (!target.HasValue)
            {
                ActionPhaseLogger.Log(unit, tool.Id, "W2_PrecheckOk");
                ActionPhaseLogger.Log(unit, tool.Id, "W2_PreDeductCheckFail", "(reason=targetInvalid)");
                ActionPhaseLogger.Log(unit, tool.Id, "W2_ConfirmAbort", "(reason=targetInvalid)");
                CleanupAfterAbort(tool, false);
                yield break;
            }

            if (!TryBeginAim(tool, unit, out _, false))
            {
                ActionPhaseLogger.Log(unit, tool.Id, "W2_PrecheckOk");
                ActionPhaseLogger.Log(unit, tool.Id, "W2_PreDeductCheckFail", "(reason=notReady)");
                ActionPhaseLogger.Log(unit, tool.Id, "W2_ConfirmAbort", "(reason=notReady)");
                CleanupAfterAbort(tool, false);
                yield break;
            }

            ActionPhaseLogger.Log(unit, tool.Id, "W2_PrecheckOk");

            var plan = BuildPlannedCost(tool, target.Value);
            var budget = turnManager != null && unit != null ? turnManager.GetBudget(unit) : null;
            var resources = turnManager != null && unit != null ? turnManager.GetResources(unit) : null;
            var cooldowns = turnManager != null && unit != null ? turnManager.GetCooldowns(unit) : null;

            int remain = budget != null ? budget.Remaining : 0;
            int energyBefore = resources != null ? resources.Get("Energy") : 0;

            Debug.Log($"[Gate] W2 PreDeduct (move={plan.moveSecs}s/{plan.moveEnergy}, atk={plan.atkSecs}s/{plan.atkEnergy}, total={plan.TotalSeconds}s/{plan.TotalEnergy}, remain={remain}s, energy={energyBefore})");

            string failReason = null;
            if (!plan.valid)
                failReason = "targetInvalid";
            else if (budget != null && plan.TotalSeconds > 0 && !budget.HasTime(plan.TotalSeconds))
                failReason = "lackTime";
            else if (resources != null && plan.TotalEnergy > 0 && !resources.Has("Energy", plan.TotalEnergy))
                failReason = "lackEnergy";
            else if (!IsCooldownReadyForConfirm(tool, cooldowns))
                failReason = "cooldown";

            if (failReason != null)
            {
                ActionPhaseLogger.Log(unit, tool.Id, "W2_PreDeductCheckFail", $"(reason={failReason})");
                ActionPhaseLogger.Log(unit, tool.Id, "W2_ConfirmAbort", $"(reason={failReason})");
                CleanupAfterAbort(tool, false);
                yield break;
            }

            ActionPhaseLogger.Log(unit, tool.Id, "W2_ChainPromptOpen", "(count=0)");
            ActionPhaseLogger.Log(unit, tool.Id, "W2_ChainPromptAbort", "(auto-skip)");

            yield return ExecuteAndResolve(tool, unit, target.Value, budget, resources, cooldowns);
        }

        IEnumerator ExecuteAndResolve(IActionToolV2 tool, Unit unit, Hex target, ITurnBudget budget, IResourcePool resources, ICooldownSink cooldowns)
        {
            phase = PhaseState.Executing;
            _hover = null;
            tool.OnExitAim();

            int budgetBefore = budget != null ? budget.Remaining : 0;
            int energyBefore = resources != null ? resources.Get("Energy") : 0;
            ActionPhaseLogger.Log(unit, tool.Id, "W3_ExecuteBegin", $"(budgetBefore={budgetBefore}, energyBefore={energyBefore})");

            var routine = tool.OnConfirm(target);
            if (routine != null)
                yield return StartCoroutine(routine);

            ActionPhaseLogger.Log(unit, tool.Id, "W3_ExecuteEnd");

            Resolve(unit, tool, budget, resources, cooldowns);
        }

        void Resolve(Unit unit, IActionToolV2 tool, ITurnBudget budget, IResourcePool resources, ICooldownSink cooldowns)
        {
            if (tool is not IActionExecReportV2 exec)
            {
                CleanupAfterAbort(tool, false);
                return;
            }

            int used = Mathf.Max(0, exec.UsedSeconds);
            int refunded = Mathf.Max(0, exec.RefundedSeconds);
            int energyMove = 0;
            int energyAtk = 0;
            bool freeMove = false;
            bool attackAdjust = false;

            if (tool is HexClickMover mover)
            {
                energyMove = mover.ReportEnergyMoveNet;
                energyAtk = mover.ReportEnergyAtkNet;
                freeMove = mover.ReportFreeMoveApplied;
            }
            else if (tool is AttackControllerV2 attack)
            {
                energyMove = attack.ReportEnergyMoveNet;
                energyAtk = attack.ReportEnergyAtkNet;
                freeMove = attack.ReportFreeMoveApplied;
                attackAdjust = attack.ReportAttackRefundSeconds > 0 || energyAtk < 0;
            }

            int net = Mathf.Max(0, used - refunded);
            ActionPhaseLogger.Log(unit, tool.Id, "W4_ResolveBegin", $"(used={used}, refunded={refunded}, net={net}, energyMove={energyMove}, energyAtk={energyAtk})");

            if (budget != null)
            {
                if (net > 0)
                {
                    int before = budget.Remaining;
                    budget.SpendTime(net);
                    int after = budget.Remaining;
                    Debug.Log($"[Time] Spend 1P {net}s -> Remain={after}");
                }

                if (net == 0 && refunded > 0)
                {
                    int before = budget.Remaining;
                    budget.RefundTime(refunded);
                    int after = budget.Remaining;
                    Debug.Log($"[Time] Refund 1P {refunded}s (reason=Adjust) -> Remain={after}");
                }

                if (freeMove)
                {
                    int before = budget.Remaining;
                    budget.RefundTime(1);
                    int after = budget.Remaining;
                    Debug.Log($"[Time] Refund 1P 1s (reason=FreeMove) -> Remain={after}");
                }
            }

            if (resources != null)
            {
                int moveEnergyRate = ResolveMoveEnergyRate(tool);
                int energyMoveSpend = energyMove > 0 ? energyMove : 0;
                int energyMoveRefund = energyMove < 0 ? -energyMove : 0;
                if (freeMove && moveEnergyRate > 0)
                    energyMoveRefund += moveEnergyRate;

                if (energyMoveSpend > 0)
                    resources.Spend("Energy", energyMoveSpend, "Move");
                if (energyMoveRefund > 0)
                    resources.Refund("Energy", energyMoveRefund, "Move");

                int netEnergyMove = energyMoveSpend - energyMoveRefund;
                if (energyMoveSpend > 0 || energyMoveRefund > 0 || freeMove)
                {
                    int after = resources.Get("Energy");
                    int logAmount = Mathf.Abs(netEnergyMove);
                    if (freeMove && logAmount == 0 && moveEnergyRate > 0)
                        logAmount = moveEnergyRate;
                    string verb = netEnergyMove >= 0 ? "Spend" : "Refund";
                    if (freeMove && netEnergyMove <= 0)
                        verb = "Refund";
                    string suffix = freeMove ? "Move_FreeMove" : "Move";
                    Debug.Log($"[Res] {verb} 1P:Energy {logAmount} -> {after} ({suffix})");
                }

                if (energyAtk != 0 || attackAdjust)
                {
                    int atkSpend = energyAtk > 0 ? energyAtk : 0;
                    int atkRefund = energyAtk < 0 ? -energyAtk : 0;
                    if (atkSpend > 0)
                        resources.Spend("Energy", atkSpend, "Attack");
                    if (atkRefund > 0)
                        resources.Refund("Energy", atkRefund, "Attack");

                    int netEnergyAtk = atkSpend - atkRefund;
                    int after = resources.Get("Energy");
                    int atkLogAmount = Mathf.Abs(netEnergyAtk);
                    string verb = netEnergyAtk >= 0 ? "Spend" : "Refund";
                    string suffix = attackAdjust ? "Attack_Adjust" : "Attack";
                    Debug.Log($"[Res] {verb} 1P:Energy {atkLogAmount} -> {after} ({suffix})");
                }
            }

            int budgetAfter = budget != null ? budget.Remaining : 0;
            int energyAfter = resources != null ? resources.Get("Energy") : 0;
            ActionPhaseLogger.Log(unit, tool.Id, "W4_ResolveEnd", $"(budgetAfter={budgetAfter}, energyAfter={energyAfter})");

            exec.Consume();
            _activeTool = null;
            _hover = null;
            phase = PhaseState.Idle;
        }

        void CleanupAfterAbort(IActionToolV2 tool, bool logCancel)
        {
            if (tool == null) return;
            if (logCancel && phase == PhaseState.Aiming)
            {
                var unit = ResolveUnit(tool);
                ActionPhaseLogger.Log(unit, tool.Id, "W1_AimCancel");
            }
            tool.OnExitAim();
            if (_activeTool == tool)
                _activeTool = null;
            _hover = null;
            phase = PhaseState.Idle;
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
                var (secs, energy) = mover.GetPlannedCost();
                return new PlannedCost
                {
                    moveSecs = Mathf.Max(0, secs),
                    moveEnergy = Mathf.Max(0, energy),
                    atkSecs = 0,
                    atkEnergy = 0,
                    valid = true
                };
            }

            if (tool is AttackControllerV2 attack)
            {
                var planned = attack.GetBaselineCost();
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

        int ResolveMoveEnergyRate(IActionToolV2 tool)
        {
            return tool switch
            {
                HexClickMover mover when mover.config != null => Mathf.Max(0, mover.config.energyCost),
                AttackControllerV2 attack when attack.moveConfig != null => Mathf.Max(0, attack.moveConfig.energyCost),
                _ => 0
            };
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

            var hex = authoring.Layout.Hex(hit.point);
            return hex;
        }
    }
}
