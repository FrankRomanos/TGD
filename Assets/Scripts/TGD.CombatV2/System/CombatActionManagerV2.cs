using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public enum ActionModeV2 { Idle, MoveAim, AttackAim, Busy }

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
        readonly Dictionary<string, IActionToolV2> _toolById = new();
        IActionToolV2 _activeTool;
        Hex? _hover;

        void Awake()
        {
            foreach (var mb in tools)
            {
                if (!mb) continue;
                if (mb is IActionToolV2 tool && !_toolById.ContainsKey(tool.Id))
                    _toolById.Add(tool.Id, tool);
            }
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

            var budget = turnManager.GetBudget(unit);
            var resources = turnManager.GetResources(unit);
            var cooldowns = turnManager.GetCooldowns(unit);

            if (tool is HexClickMover mover)
            {
                var cfg = mover.config;
                string actionId = ResolveActionId(mover);
                if (cooldowns != null && !cooldowns.Ready(actionId))
                {
                    HexMoveEvents.RaiseRejected(unit, MoveBlockReason.OnCooldown, null);
                    return false;
                }

                int energyNeed = Mathf.Max(0, cfg != null ? cfg.energyCost : 0);
                if (resources != null && energyNeed > 0 && !resources.Has("Energy", energyNeed))
                {
                    HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NotEnoughResource, null);
                    return false;
                }

                int timeNeed = Mathf.Max(0, Mathf.CeilToInt(cfg != null ? cfg.timeCostSeconds : 0f));
                if (budget != null && timeNeed > 0 && !budget.HasTime(timeNeed))
                {
                    HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NoBudget, null);
                    return false;
                }
            }
            else if (tool is AttackControllerV2 attack)
            {
                var cfg = attack.attackConfig;
                string actionId = cfg != null ? cfg.name : attack.Id;
                if (cooldowns != null && !cooldowns.Ready(actionId))
                {
                    AttackEventsV2.RaiseRejected(unit, AttackRejectReasonV2.OnCooldown, "On cooldown.");
                    return false;
                }

                int energyNeed = cfg != null ? Mathf.CeilToInt(cfg.baseEnergyCost) : 0;
                if (resources != null && energyNeed > 0 && !resources.Has("Energy", energyNeed))
                {
                    AttackEventsV2.RaiseRejected(unit, AttackRejectReasonV2.NotEnoughResource, "Not enough energy.");
                    return false;
                }
            }

            return true;
        }

        void ApplyTurnBudgets(Unit unit, IActionToolV2 tool)
        {
            var budget = turnManager != null ? turnManager.GetBudget(unit) : null;
            if (budget == null) return;

            int spend = EstimateUsedSeconds(tool);
            if (spend > 0) budget.SpendTime(spend);

            int refund = EstimateRefundSeconds(tool);
            if (refund > 0) budget.RefundTime(refund);
        }

        int EstimateUsedSeconds(IActionToolV2 tool)
        {
            if (tool is HexClickMover mover && mover.config != null)
                return Mathf.Max(0, Mathf.CeilToInt(mover.config.timeCostSeconds));
            if (tool is AttackControllerV2 attack && attack.attackConfig != null)
                return Mathf.Max(0, attack.attackConfig.baseTimeSeconds);
            return 0;
        }

        int EstimateRefundSeconds(IActionToolV2 tool) => 0;

        string ResolveActionId(HexClickMover mover)
        {
            if (mover == null) return "Move";
            if (!string.IsNullOrEmpty(mover.actionIdOverride))
                return mover.actionIdOverride;
            return mover.config != null ? mover.config.actionId : mover.Id;
        }

        // ===== 外部 UI 也可以直接用这俩 API =====
        public void RequestAim(string toolId)
        {
            // 忙碌期不能开启新动作
            if (_mode == ActionModeV2.Busy) return;

            // 重复按：切换回 Idle
            if (_activeTool != null && _activeTool.Id == toolId)
            {
                Cancel(); return;
            }

            // 切换工具：先取消旧的
            if (_activeTool != null) Cancel();

            if (!_toolById.TryGetValue(toolId, out var tool)) return;

            _activeTool = tool;
            _hover = null;
            _activeTool.OnEnterAim();

            _mode = (toolId == "Attack") ? ActionModeV2.AttackAim : ActionModeV2.MoveAim;
        }

        public void Cancel()
        {
            if (_activeTool != null)
            {
                _activeTool.OnExitAim();
                _activeTool = null;
            }
            _hover = null;
            _mode = ActionModeV2.Idle;
        }

        public void Confirm()
        {
            if (_activeTool == null) return;
            var h = _hover ?? PickHexUnderMouse();
            if (!h.HasValue) return;

            // 进入 Busy：不再响应其他按键，直到协程结束
            var unit = ResolveUnit(_activeTool);
            if (!Precheck(unit, _activeTool)) return;

            StartCoroutine(RunBusy(_activeTool, h.Value, unit));
        }

        IEnumerator RunBusy(IActionToolV2 tool, Hex h, Unit unit)
        {
            _mode = ActionModeV2.Busy;
            _hover = null;

            yield return tool.OnConfirm(h);   // 工具执行（移动/靠近等）
            if (turnManager != null && unit != null)
                ApplyTurnBudgets(unit, tool);
            tool.OnExitAim();
            _hover = null;
            // 执行完毕：恢复 Idle
            if (_activeTool == tool) _activeTool = null;
            _mode = ActionModeV2.Idle;
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
