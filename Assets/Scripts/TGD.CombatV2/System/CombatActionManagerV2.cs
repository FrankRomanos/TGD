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
        public List<MonoBehaviour> tools = new();  // �� ClickMover, AttackControllerV2 ��

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
            // ���� ģʽ�л������⣩����
            if (_mode != ActionModeV2.Busy)
            {
                if (Input.GetKeyDown(keyMoveAim)) RequestAim("Move");
                if (Input.GetKeyDown(keyAttackAim)) RequestAim("Attack");
            }

            // ���� ��׼�У�Hover / Confirm / Cancel ���� 
            if (_mode == ActionModeV2.MoveAim || _mode == ActionModeV2.AttackAim)
            {
                var h = PickHexUnderMouse();
                if (h.HasValue && (!_hover.HasValue || !_hover.Value.Equals(h.Value)))
                {
                    _hover = h;
                    _activeTool?.OnHover(h.Value);
                }

                if (Input.GetMouseButtonDown(0)) Confirm();      // ���ȷ��
                if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) Cancel(); // �Ҽ�/ESC ȡ��
            }
        }

        // ===== �ⲿ UI Ҳ����ֱ�������� API =====
        public void RequestAim(string toolId)
        {
            // æµ�ڲ��ܿ����¶���
            if (_mode == ActionModeV2.Busy) return;

            // �ظ������л��� Idle
            if (_activeTool != null && _activeTool.Id == toolId)
            {
                Cancel(); return;
            }

            // �л����ߣ���ȡ���ɵ�
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

            // ���� Busy��������Ӧ����������ֱ��Э�̽���
            StartCoroutine(RunBusy(_activeTool, h.Value));
        }

        IEnumerator RunBusy(IActionToolV2 tool, Hex h)
        {
            _mode = ActionModeV2.Busy;
            _hover = null;

            yield return tool.OnConfirm(h);   // ����ִ�У��ƶ�/�����ȣ�
            tool.OnExitAim();
            _hover = null;
            // ִ����ϣ��ָ� Idle
            if (_activeTool == tool) _activeTool = null;
            _mode = ActionModeV2.Idle;
        }

        // ===== ʰȡͳһ�� Manager ����һ���޾�ȫ�� =====
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
