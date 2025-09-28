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
            // 只响应自己这个 mover 的单位
            return mover != null && u == mover.driver?.UnitRef;
        }

        void OnStarted(Unit u, System.Collections.Generic.List<Hex> path)
        {
            if (!Match(u) || !animator) return;
            // 关键：移动期间关闭 Root Motion，避免双重位移
            animator.applyRootMotion = false;
            animator.SetBool(_runningId, true);
        }

        void OnFinished(Unit u, Hex end)
        {
            if (!Match(u) || !animator) return;
            animator.SetBool(_runningId, false);
            // 保持关闭（网格游戏通常不用 Root Motion 位移）
            animator.applyRootMotion = false;
        }
    }
}
