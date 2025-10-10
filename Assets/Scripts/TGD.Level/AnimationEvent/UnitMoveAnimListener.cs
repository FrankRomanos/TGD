using UnityEngine;
using TGD.CombatV2;

namespace TGD.HexBoard
{
    [DisallowMultipleComponent]
    public sealed class UnitMoveAnimListener : MonoBehaviour
    {
        [Header("Refs")]
        public HexClickMover mover;
        public Animator animator;
        public HexBoardTestDriver driver;     // ★ 新增：用于匹配 Unit

        public string runningBoolName = "IsRunning";

        [Header("Root Motion 策略")]
        public bool manageRootMotion = true;
        public bool rootMotionForNormalMove = true;
        public bool rootMotionForAttackMove = false;

        int _runningId;
        bool _attackMoveActive;
        bool _prevRM;

        void Reset()
        {
            if (!mover) mover = GetComponent<HexClickMover>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!driver) driver = GetComponentInParent<HexBoardTestDriver>();
        }

        void Awake()
        {
            _runningId = Animator.StringToHash(runningBoolName);
            if (!driver) driver = GetComponentInParent<HexBoardTestDriver>();
        }

        void OnEnable()
        {
            HexMoveEvents.MoveStarted += OnMoveStarted;
            HexMoveEvents.MoveFinished += OnMoveFinished;

            AttackEventsV2.AttackMoveStarted += OnAttackMoveStarted;
            AttackEventsV2.AttackMoveFinished += OnAttackMoveFinished;
        }

        void OnDisable()
        {
            HexMoveEvents.MoveStarted -= OnMoveStarted;
            HexMoveEvents.MoveFinished -= OnMoveFinished;

            AttackEventsV2.AttackMoveStarted -= OnAttackMoveStarted;
            AttackEventsV2.AttackMoveFinished -= OnAttackMoveFinished;

            _attackMoveActive = false;
        }

        // ★ 统一按 Unit 过滤（优先 driver.UnitRef；次选 mover.driver.UnitRef）
        bool Match(Unit u)
        {
            var my = driver ? driver.UnitRef : mover?.driver?.UnitRef;
            return my != null && u == my;
        }

        void OnAttackMoveStarted(Unit u, System.Collections.Generic.List<Hex> _)
        {
            if (Match(u)) _attackMoveActive = true;
        }

        void OnAttackMoveFinished(Unit u, Hex _)
        {
            if (Match(u)) _attackMoveActive = false;
        }

        void OnMoveStarted(Unit u, System.Collections.Generic.List<Hex> _)
        {
            if (!Match(u) || !animator) return;

            if (manageRootMotion)
            {
                _prevRM = animator.applyRootMotion;
                animator.applyRootMotion = _attackMoveActive ? rootMotionForAttackMove
                                                             : rootMotionForNormalMove;
            }
            animator.SetBool(_runningId, true);
        }

        void OnMoveFinished(Unit u, Hex _)
        {
            if (!Match(u) || !animator) return;

            animator.SetBool(_runningId, false);

            if (manageRootMotion)
                animator.applyRootMotion = rootMotionForNormalMove;

            _attackMoveActive = false;
        }
    }
}
