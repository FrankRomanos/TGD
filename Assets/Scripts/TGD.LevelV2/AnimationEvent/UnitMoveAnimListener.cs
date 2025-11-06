using UnityEngine;
using TGD.CombatV2;
using TGD.HexBoard;
using TGD.CoreV2;

namespace TGD.LevelV2
{
    [DisallowMultipleComponent]
    public sealed class UnitMoveAnimListener : MonoBehaviour
    {
        [Header("Refs")]
        public HexClickMover mover;
        public Animator animator;
        public UnitRuntimeContext ctx;

        public string runningBoolName = "IsRunning";

        [Header("Root Motion 策略")]
        public bool manageRootMotion = true;
        public bool rootMotionForNormalMove = true;

        int _runningId;
        bool _prevRM;

        void Reset()
        {
            if (!mover) mover = GetComponent<HexClickMover>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!ctx) ctx = GetComponentInParent<UnitRuntimeContext>(true);
        }

        void Awake()
        {
            _runningId = Animator.StringToHash(runningBoolName);
            if (!ctx) ctx = GetComponentInParent<UnitRuntimeContext>(true);
            if (animator) _prevRM = animator.applyRootMotion;
        }

        void OnEnable()
        {
            HexMoveEvents.MoveStarted += OnMoveStarted;
            HexMoveEvents.MoveFinished += OnMoveFinished;
        }

        void OnDisable()
        {
            HexMoveEvents.MoveStarted -= OnMoveStarted;
            HexMoveEvents.MoveFinished -= OnMoveFinished;

            if (animator && manageRootMotion)
                animator.applyRootMotion = _prevRM;
        }

        // ★ 统一按 Unit 过滤（优先 ctx.boundUnit；次选 mover.ctx.boundUnit）
        bool Match(Unit u)
        {
            if (u == null)
                return false;

            Unit owner = null;
            if (ctx != null && ctx.boundUnit != null)
                owner = ctx.boundUnit;
            else if (mover != null && mover.ctx != null && mover.ctx.boundUnit != null)
                owner = mover.ctx.boundUnit;

            return owner != null && u == owner;
        }

        void OnMoveStarted(Unit u, System.Collections.Generic.List<Hex> _)
        {
            if (!Match(u) || !animator) return;

            if (manageRootMotion)
            {
                _prevRM = animator.applyRootMotion;
                animator.applyRootMotion = rootMotionForNormalMove;
            }
            animator.SetBool(_runningId, true);
        }

        void OnMoveFinished(Unit u, Hex _)
        {
            if (!Match(u) || !animator) return;

            animator.SetBool(_runningId, false);

            if (manageRootMotion)
                animator.applyRootMotion = _prevRM;
        }
    }
}
