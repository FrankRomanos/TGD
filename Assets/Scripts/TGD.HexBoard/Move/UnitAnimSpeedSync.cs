using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    /// <summary>
    /// 根据 HexClickMover 的实际移动速度，自动调节 Animator.speed，
    /// 保证脚步不过快/过慢（避免“滑步”）。
    /// </summary>
    public sealed class UnitAnimSpeedSync : MonoBehaviour
    {
        [Header("Refs")]
        public HexClickMover mover;     // 同一个单位上的 mover
        public Animator animator;       // 该单位的 Animator

        [Header("Tuning")]
        [Tooltip("当 Animator.speed = 1 时，跑步动画在世界中的等效速度（米/秒）。做一次性标定后保持不变。")]
        public float runMetersPerSecond = 3.5f;

        [Tooltip("将 animator.speed 限制在此范围，避免过慢或过快。")]
        public float minAnimatorSpeed = 0.5f;
        public float maxAnimatorSpeed = 1.8f;

        [Tooltip("（可选）同步写入 Animator 的某个 float 参量，例如 RunSpeed。留空则不写。")]
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
        }

        void OnDisable()
        {
            HexMoveEvents.MoveStarted -= OnMoveStarted;
            HexMoveEvents.MoveFinished -= OnMoveFinished;
            ResetSpeed();
        }

        bool IsThisUnit(Unit u)
        {
            // 与本组件所属的 mover/driver 的 UnitRef 比较引用
            return mover != null && mover.driver != null && ReferenceEquals(u, mover.driver.UnitRef);
        }

        void OnMoveStarted(Unit u, List<Hex> path)
        {
            if (!IsThisUnit(u) || mover == null || mover.authoring == null || animator == null) return;
            if (path == null || path.Count < 2) { ResetSpeed(); return; }

            var L = mover.authoring.Layout;
            float dist = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                var p0 = L.World(path[i - 1], mover.y);
                var p1 = L.World(path[i], mover.y);
                dist += Vector3.Distance(p0, p1);
            }

            // HexClickMover 每一步用 stepSeconds，步数= path.Count-1
            float time = Mathf.Max(0.01f, mover.stepSeconds * (path.Count - 1));
            float v = dist / time; // 实际世界速度（米/秒）

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
    }
}
