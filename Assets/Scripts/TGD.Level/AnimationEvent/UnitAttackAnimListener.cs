using System.Collections;
using TGD.CombatV2;
using TGD.HexBoard;
using UnityEngine;

namespace TGD.HexBoard
{
    [DisallowMultipleComponent]
    public sealed class UnitAttackAnimListener : MonoBehaviour
    {
        public AttackControllerV2 attackController;
        public Animator animator;
        public string attack1BoolName = "IsAttack1";
        public string attack2BoolName = "IsAttack2";
        public string attack3BoolName = "IsAttack3";
        [Min(0f)] public float resetDelay = 0.2f;

        int _attack1Id;
        int _attack2Id;
        int _attack3Id;
        Coroutine _resetCo;

        void Reset()
        {
            if (!attackController) attackController = GetComponent<AttackControllerV2>();
            if (!animator) animator = GetComponentInChildren<Animator>();
        }

        void Awake()
        {
            _attack1Id = Animator.StringToHash(attack1BoolName);
            _attack2Id = Animator.StringToHash(attack2BoolName);
            _attack3Id = Animator.StringToHash(attack3BoolName);
        }

        void OnEnable()
        {
            AttackEventsV2.AttackAnimationRequested += OnAttackAnimation;
        }

        void OnDisable()
        {
            AttackEventsV2.AttackAnimationRequested -= OnAttackAnimation;
            if (_resetCo != null)
            {
                StopCoroutine(_resetCo);
                _resetCo = null;
            }
            SetAllFalse();
        }

        void OnAttackAnimation(Unit unit, int comboIndex)
        {
            if (!animator) return;
            if (!Matches(unit)) return;

            comboIndex = Mathf.Clamp(comboIndex, 1, 3);
            if (_resetCo != null) StopCoroutine(_resetCo);
            SetAllFalse();

            int targetId = comboIndex switch
            {
                1 => _attack1Id,
                2 => _attack2Id,
                _ => _attack3Id
            };

            if (targetId != 0)
                animator.SetBool(targetId, true);
            _resetCo = StartCoroutine(ResetAfterDelay());
        }

        bool Matches(Unit unit)
        {
            if (!attackController) return false;
            return unit != null && attackController.driver != null && unit == attackController.driver.UnitRef;
        }

        IEnumerator ResetAfterDelay()
        {
            yield return null;
            if (resetDelay > 0f)
                yield return new WaitForSeconds(resetDelay);
            SetAllFalse();
            _resetCo = null;
        }

        void SetAllFalse()
        {
            if (!animator) return;
            animator.SetBool(_attack1Id, false);
            animator.SetBool(_attack2Id, false);
            animator.SetBool(_attack3Id, false);
        }
    }
}