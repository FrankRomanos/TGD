using UnityEngine;

namespace TGD.HexBoard
{
    [DisallowMultipleComponent]
    public sealed class UnitMoveAnimListener : MonoBehaviour
    {
        public HexClickMover mover;
        public Animator animator;
        public string runningBoolName = "IsRunning";

        int _runningId;
        Unit _unit;

        void Reset()
        {
            if (!mover) mover = GetComponent<HexClickMover>();
            if (!animator) animator = GetComponentInChildren<Animator>();
        }

        void Awake()
        {
            _runningId = Animator.StringToHash(runningBoolName);
        }

        void OnEnable()
        {
            HexMoveEvents.MoveStarted += OnStarted;
            HexMoveEvents.MoveFinished += OnFinished;
        }

        void OnDisable()
        {
            HexMoveEvents.MoveStarted -= OnStarted;
            HexMoveEvents.MoveFinished -= OnFinished;
        }

        bool Match(Unit u)
        {
            // ֻ��Ӧ�Լ���� mover �ĵ�λ
            return mover != null && u == mover.driver?.UnitRef;
        }

        void OnStarted(Unit u, System.Collections.Generic.List<Hex> path)
        {
            if (!Match(u) || !animator) return;
            // �ؼ����ƶ��ڼ�ر� Root Motion������˫��λ��
            animator.applyRootMotion = false;
            animator.SetBool(_runningId, true);
        }

        void OnFinished(Unit u, Hex end)
        {
            if (!Match(u) || !animator) return;
            animator.SetBool(_runningId, false);
            // ���ֹرգ�������Ϸͨ������ Root Motion λ�ƣ�
            animator.applyRootMotion = false;
        }
    }
}
