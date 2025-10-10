using UnityEngine;
using TGD.CombatV2;

namespace TGD.HexBoard
{
    [DisallowMultipleComponent]
    public sealed class UnitMoveAnimListener : MonoBehaviour
    {
        public HexClickMover mover;
        public Animator animator;
        public string runningBoolName = "IsRunning";

        [Header("Root Motion 策略")]
        public bool manageRootMotion = true;          // 是否由本脚本托管 RootMotion
        public bool rootMotionForNormalMove = true;   // ✅ 精准/普通移动：是否启用 RootMotion（通常需要）
        public bool rootMotionForAttackMove = false;  // ✅ 攻击移动：是否启用 RootMotion（通常关闭，避免双重位移）

        int _runningId;
        bool _attackMoveActive;   // 当前是否处于“攻击移动”
        bool _prevRM;

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
            // 普通/精准移动事件
            HexMoveEvents.MoveStarted += OnMoveStarted;
            HexMoveEvents.MoveFinished += OnMoveFinished;

            // 攻击移动事件（只用来标记类型，不直接驱动动画）
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

        bool Match(Unit u) => mover != null && u == mover.driver?.UnitRef;

        // 标记/取消标记“攻击移动”
        void OnAttackMoveStarted(Unit u, System.Collections.Generic.List<Hex> path)
        {
            if (Match(u)) _attackMoveActive = true;
        }

        void OnAttackMoveFinished(Unit u, Hex end)
        {
            if (Match(u)) _attackMoveActive = false;
        }

        // 统一用 HexMoveEvents 驱动跑步开关，但 RootMotion 根据是否攻击移动来决定
        void OnMoveStarted(Unit u, System.Collections.Generic.List<Hex> path)
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

        void OnMoveFinished(Unit u, Hex end)
        {
            if (!Match(u) || !animator) return;

            animator.SetBool(_runningId, false);

            if (manageRootMotion)
            {
                // 结束后恢复到“普通移动”的默认 RM 策略（或也可恢复至 _prevRM，看你的项目规范）
                animator.applyRootMotion = rootMotionForNormalMove;
            }

            _attackMoveActive = false;
        }
    }
}
