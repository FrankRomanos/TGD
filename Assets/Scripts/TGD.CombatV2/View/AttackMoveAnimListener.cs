// File: TGD.CombatV2/View/AttackMoveAnimListener.cs
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class AttackMoveAnimListener : MonoBehaviour
    {
        public Animator animator;
        public HexBoardTestDriver driver;
        public string runningBoolName = "IsRunning";
        public bool manageRootMotion = true;
        public bool rootMotionForAttackMove = false;

        int _runningId;
        bool _prevRM;
        Unit UnitRef => driver ? driver.UnitRef : null;
        bool Match(Unit u) => u != null && u == UnitRef;

        void Reset()
        {
            if (!animator) animator = GetComponentInChildren<Animator>(true);
            if (!driver) driver = GetComponentInParent<HexBoardTestDriver>();
        }

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>(true);
            if (!driver) driver = GetComponentInParent<HexBoardTestDriver>();
            _runningId = Animator.StringToHash(runningBoolName);
        }

        void OnEnable()
        {
            AttackEventsV2.AttackMoveStarted += OnStarted;
            AttackEventsV2.AttackMoveFinished += OnFinished;
        }

        void OnDisable()
        {
            AttackEventsV2.AttackMoveStarted -= OnStarted;
            AttackEventsV2.AttackMoveFinished -= OnFinished;
        }

        void OnStarted(Unit u, List<Hex> _)
        {
            if (!Match(u) || !animator) return;
            if (manageRootMotion)
            {
                _prevRM = animator.applyRootMotion;
                animator.applyRootMotion = rootMotionForAttackMove;
            }
            animator.SetBool(_runningId, true);
        }

        void OnFinished(Unit u, Hex _)
        {
            if (!Match(u) || !animator) return;
            animator.SetBool(_runningId, false);
            if (manageRootMotion)
                animator.applyRootMotion = _prevRM;
        }
    }
}
