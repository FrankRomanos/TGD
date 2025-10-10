// File: TGD.CombatV2/View/AttackAnimDriver.cs
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class AttackAnimDriver : MonoBehaviour
    {
        public Animator anim;
          [Header("Optional")]
    public HexBoardTestDriver driver;         // 允许拖引用（没有也行）
    public bool requireUnitMatch = false;     // 默认不强制校验 Unit

        [Header("State Names")]
        public string stateAttack1 = "Attack1";
        public string stateAttack2 = "Attack2";
        public string stateAttack3 = "Attack3";
        public string stateJump = "jumpattack"; // 4段及以上用这个

        [Header("Play")]
        public float crossFade = 0.06f;

        bool _busy;
        int _currentCombo;
        Unit _unit;

        public void BindUnit(Unit unit) => _unit = unit;

        void Awake()
        {
            if (!anim)
                anim = GetComponentInChildren<Animator>(true);

            ResolveUnit();
        }

        void OnEnable()
        {
            ResolveUnit();
            AttackEventsV2.AttackAnimationRequested += OnAnimRequested;
        }

        void OnDisable()
        {
            AttackEventsV2.AttackAnimationRequested -= OnAnimRequested;
            _busy = false;
            _currentCombo = 0;
        }

        void OnAnimRequested(Unit unit, int combo)
        {
            // 先确保有 Animator
            if (anim == null) return;

            // 只有在需要严格校验时才比较 Unit
            if (requireUnitMatch)
            {
                ResolveUnit();
                if (_unit == null || _unit != unit) return;
            }

            if (_busy) return;

            _currentCombo = combo;
            string state = combo >= 4 ? stateJump :
                           (combo == 1 ? stateAttack1 :
                           (combo == 2 ? stateAttack2 : stateAttack3));

            _busy = true;
            anim.CrossFadeInFixedTime(state, crossFade, 0, 0f);
        }

        // AnimationEvent hook: called from attack clips on the strike frame
        public void Anim_Strike()
        {
            if (_unit != null)
                AttackEventsV2.RaiseAttackStrike(_unit, _currentCombo);
        }

        // AnimationEvent hook: called from attack clips when the animation ends
        public void Anim_End()
        {
            _busy = false;
            if (_unit != null)
                AttackEventsV2.RaiseAttackAnimEnded(_unit, _currentCombo);
            _currentCombo = 0;
        }

        void ResolveUnit()
        {
            if (_unit != null) return;

            // ✅ 先用 inspector 里拖的 driver；没有再向上找
            var drv = driver != null ? driver : GetComponentInParent<HexBoardTestDriver>();
            if (drv != null)
            {
                drv.EnsureInit();
                _unit = drv.UnitRef;
            }
        }
    }
}
