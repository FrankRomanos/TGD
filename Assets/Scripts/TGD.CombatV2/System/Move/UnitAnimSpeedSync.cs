using System.Buffers.Text;
using System.Collections.Generic;
using TGD.HexBoard;
using UnityEngine;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    /// <summary>
    /// Syncs the animator speed with the actual travel speed of HexClickMover.
    /// </summary>
    public sealed class UnitAnimSpeedSync : MonoBehaviour
    {
        [Header("Refs")]
        public HexClickMover mover;     // 同一世界系 mover
        public Animator animator;       // 对应角色 Animator

        [Header("Tuning")]
        [Tooltip("Animator.speed = 1 时对应的期望跑步速度（米/秒）。")]
        public float runMetersPerSecond = 3.5f;

        [Tooltip("Animator.speed 的夹取范围。")]
        public float minAnimatorSpeed = 0.5f;
        public float maxAnimatorSpeed = 1.8f;

        [Tooltip("可选：同步写入 Animator 某个 float 参数（如 RunSpeed）。")]
        public string animatorSpeedParam;

        void Reset()
        {
            if (!animator) TryGetComponent(out animator);
            if (!mover) TryGetComponent(out mover);
        }

        void OnEnable()
        {
            HexMoveEvents.MoveStarted += OnMoveStarted;
            HexMoveEvents.MoveFinished += OnMoveFinished;
            HexMoveEvents.StepSpeedHint += OnStepSpeedHint;
        }

        void OnDisable()
        {
            HexMoveEvents.MoveStarted -= OnMoveStarted;
            HexMoveEvents.MoveFinished -= OnMoveFinished;
            HexMoveEvents.StepSpeedHint -= OnStepSpeedHint;
            ResetSpeed();
        }

        bool IsThisUnit(Unit u)
        {
            if (mover == null)
                return false;

            var bound = mover.OwnerUnit;
            return bound != null && ReferenceEquals(u, bound);
        }

        void OnMoveStarted(Unit u, List<Hex> path)
        {
            if (!IsThisUnit(u) || mover == null || animator == null) return;
            // 开始时重置速度；StepSpeedHint 会细调
            ResetSpeed();
            if (path == null || path.Count < 2) { ResetSpeed(); return; }

            var hexSpace = HexSpace.Instance;
            if (hexSpace == null) return;

            float dist = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                var p0 = hexSpace.HexToWorld(path[i - 1], mover.y);
                var p1 = hexSpace.HexToWorld(path[i], mover.y);
                dist += Vector3.Distance(p0, p1);
            }

            // HexClickMover 的每一步耗时固定 stepSeconds
            float time = Mathf.Max(0.01f, mover.stepSeconds * (path.Count - 1));
            float v = dist / time; // 实际位移速度（米/秒）

            float baseMps = Mathf.Max(0.01f, runMetersPerSecond);
            float sp = Mathf.Clamp(v / baseMps, minAnimatorSpeed, maxAnimatorSpeed);

            animator.speed = sp;
            if (!string.IsNullOrEmpty(animatorSpeedParam))
                animator.SetFloat(animatorSpeedParam, sp);
        }

        void OnMoveFinished(Unit u, Hex end)
        {
            if (!IsThisUnit(u)) return;
            ResetSpeed();
        }

        void ResetSpeed()
        {
            if (animator == null) return;
            animator.speed = 1f;
            if (!string.IsNullOrEmpty(animatorSpeedParam))
                animator.SetFloat(animatorSpeedParam, 1f);
        }
        void OnStepSpeedHint(Unit u, float effMR, float baseMR)
        {
            if (!IsThisUnit(u) || animator == null) return;
            // 根据 MoveRate 差异微调速度
            float ratio = (baseMR <= 0f) ? 1f : Mathf.Max(0.01f, effMR / baseMR);
            float sp = Mathf.Clamp(ratio, minAnimatorSpeed, maxAnimatorSpeed);
            animator.speed = sp;
            if (!string.IsNullOrEmpty(animatorSpeedParam))
                animator.SetFloat(animatorSpeedParam, sp);
        }
    }
}
