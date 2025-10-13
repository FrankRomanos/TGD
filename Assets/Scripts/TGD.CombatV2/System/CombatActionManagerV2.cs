using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public enum ActionModeV2 { Idle, MoveAim, AttackAim, ChainPrompt, Busy }

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

        [Header("Keybinds")]
        public KeyCode keyMoveAim = KeyCode.V;
        public KeyCode keyAttackAim = KeyCode.A;

        ActionModeV2 _mode = ActionModeV2.Idle;
        readonly Dictionary<string, List<IActionToolV2>> _toolsById = new();
        Unit _currentUnit; // ★ 记录当前回合单位
        IActionToolV2 _activeTool;
        Hex? _hover;

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
            if (turnManager != null) turnManager.TurnStarted += OnTurnStarted;
        }
        void OnDisable()
        {
            if (turnManager != null) turnManager.TurnStarted -= OnTurnStarted;
        }
        void OnTurnStarted(TGD.HexBoard.Unit u)
        {
            _currentUnit = u;
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
                if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) Cancel(); // 右键/ESC 取消
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

        void ApplyExecution(Unit unit, IActionToolV2 tool)
        {
            if (turnManager == null || unit == null) return;
            if (tool is not IActionExecReportV2 exec) return;
            int used = Mathf.Max(0, exec.UsedSeconds);
            int refunded = Mathf.Max(0, exec.RefundedSeconds);

            var budget = turnManager.GetBudget(unit);
            if (budget != null)
            {
                if (used > 0) budget.SpendTime(used);
                if (refunded > 0) budget.RefundTime(refunded);
            }
            var resources = turnManager.GetResources(unit);
            if (resources != null)
            {
                switch (tool)
                {
                    case HexClickMover mover when mover.config != null:
                        {
                            int rate = Mathf.Max(0, mover.config.energyCost);
                            if (rate > 0)
                            {
                                if (used > 0) resources.Spend("Energy", used * rate, "Move");
                                if (refunded > 0) resources.Refund("Energy", refunded * rate, "MoveRefund");
                            }
                            break;
                        }
                    case AttackControllerV2 attack when attack.attackConfig != null:
                        {
                            int moveEnergy = Mathf.Max(0, attack.ReportMoveEnergyNet);
                            int attackEnergy = Mathf.Max(0, attack.ReportAttackEnergyNet);
                            if (moveEnergy > 0) resources.Spend("Energy", moveEnergy, "AttackMove");
                            if (attackEnergy > 0) resources.Spend("Energy", attackEnergy, "Attack");
                            break;
                        }
                }
            }
            exec.Consume();
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
                if (userInitiated && (_mode == ActionModeV2.MoveAim || _mode == ActionModeV2.AttackAim))
                    ActionPhaseLogger.Log(ResolveUnit(_activeTool), _activeTool.Id, ActionPhase.W1_AimCancel);
                _activeTool.OnExitAim();
                _activeTool = null;
            }
            _hover = null;
            _mode = ActionModeV2.Idle;
        }
        void AbortConfirm(IActionToolV2 tool, Unit unit, string reason)
        {
            ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W2_ConfirmAbort, reason);
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

            int needTime = 0;
            int needEnergy = 0;
            string okMessage = "(min-plan)";

            if (tool is HexClickMover mover)
            {
                var (timeSec, energy) = mover.PeekPlannedCost();
                needTime = Mathf.Max(0, timeSec);
                needEnergy = Mathf.Max(0, energy);
                okMessage = $"(time>={needTime}s, energy>={needEnergy})";
            }
            else if (tool is AttackControllerV2 attack)
            {
                var planned = attack.PeekPlannedCost(hex);
                int moveSecs = Mathf.Max(0, planned.moveSecs);
                int moveEnergy = Mathf.Max(0, planned.moveEnergy);
                int atkSecs = Mathf.Max(0, planned.atkSecs);
                int atkEnergy = Mathf.Max(0, planned.atkEnergy);
                needTime = moveSecs + atkSecs;
                needEnergy = moveEnergy + atkEnergy;
                okMessage = planned.valid
                    ? $"(move={moveSecs}s/{moveEnergy}, atk={atkSecs}s/{atkEnergy}, total={needTime}s/{needEnergy})"
                    : $"(min-plan move={moveSecs}s/{moveEnergy}, atk={atkSecs}s/{atkEnergy})";
            }

            bool ok = true;
            if (turnManager != null && unit != null)
            {
                var budget = turnManager.GetBudget(unit);
                if (budget != null && needTime > 0)
                    ok &= budget.Remaining >= needTime;

                var resources = turnManager.GetResources(unit);
                if (resources != null && needEnergy > 0)
                    ok &= resources.Get("Energy") >= needEnergy;
            }

            ActionPhaseLogger.Log(unit, tool.Id,
                ok ? ActionPhase.W2_PreDeductCheckOk : ActionPhase.W2_PreDeductCheckFail,
                ok ? okMessage : "(lack)");

            if (!ok)
            {
                AbortConfirm(tool, unit, "(pre-deduct)");
                yield break;
            }

            _mode = ActionModeV2.ChainPrompt;
            ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W2_ChainPromptOpen, "(stub)");

            bool autoSkipLogged = false;
            while (_mode == ActionModeV2.ChainPrompt)
            {
                yield return null;

                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                {
                    ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W2_ChainPromptCancelled);
                    break;
                }

                if (!autoSkipLogged)
                {
                    ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W2_ChainPromptAutoSkip);
                    autoSkipLogged = true;
                }

                break;
            }

            _mode = ActionModeV2.Busy;
            yield return RunBusy(tool, hex, unit);
        }
        IEnumerator RunBusy(IActionToolV2 tool, Hex h, Unit unit)
        {
            _mode = ActionModeV2.Busy;
            _hover = null;

            ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W3_ExecuteBegin);
            yield return tool.OnConfirm(h);   // 工具执行（移动/靠近等）
            ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W3_ExecuteEnd);

            ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W4_ResolveBegin);
            ApplyExecution(unit, tool);
            ActionPhaseLogger.Log(unit, tool.Id, ActionPhase.W4_ResolveEnd);
            if (_activeTool == tool)
                ExitActiveTool(false);
            else
            {
                tool.OnExitAim();
                _hover = null;
                _mode = ActionModeV2.Idle;
            }
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
