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
            StartCoroutine(RunBusy(_activeTool, h.Value));
        }

        IEnumerator RunBusy(IActionToolV2 tool, Hex h)
        {
            _mode = ActionModeV2.Busy;
            _hover = null;

            yield return tool.OnConfirm(h);   // 工具执行（移动/靠近等）
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
