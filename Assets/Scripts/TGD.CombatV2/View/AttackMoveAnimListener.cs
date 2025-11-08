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
        public UnitRuntimeContext ctx;
        public string runningBoolName = "IsRunning";
        public bool manageRootMotion = true;
        public bool rootMotionForAttackMove = false;

        int _runningId;
        bool _prevRM;
        AttackControllerV2 _attack;
        Unit OwnerUnit
        {
            get
            {
                if (ctx != null && ctx.boundUnit != null)
                    return ctx.boundUnit;
                if (_attack != null && _attack.Ctx != null && _attack.Ctx.boundUnit != null)
                {
                    ctx = _attack.Ctx;
                    return ctx.boundUnit;
                }
                return null;
            }
        }
        bool Match(Unit u) => u != null && u == OwnerUnit;

        void Reset()
        {
            if (!animator) animator = GetComponentInChildren<Animator>(true);
            if (!ctx) ctx = GetComponentInParent<UnitRuntimeContext>(true);
            if (_attack == null) _attack = GetComponentInParent<AttackControllerV2>(true);
        }

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>(true);
            if (!ctx) ctx = GetComponentInParent<UnitRuntimeContext>(true);
            if (_attack == null) _attack = GetComponentInParent<AttackControllerV2>(true);
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
