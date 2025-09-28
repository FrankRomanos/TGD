using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    /// <summary>
    /// ���� HexClickMover ��ʵ���ƶ��ٶȣ��Զ����� Animator.speed��
    /// ��֤�Ų�������/���������⡰����������
    /// </summary>
    public sealed class UnitAnimSpeedSync : MonoBehaviour
    {
        [Header("Refs")]
        public HexClickMover mover;     // ͬһ����λ�ϵ� mover
        public Animator animator;       // �õ�λ�� Animator

        [Header("Tuning")]
        [Tooltip("�� Animator.speed = 1 ʱ���ܲ������������еĵ�Ч�ٶȣ���/�룩����һ���Ա궨�󱣳ֲ��䡣")]
        public float runMetersPerSecond = 3.5f;

        [Tooltip("�� animator.speed �����ڴ˷�Χ�������������졣")]
        public float minAnimatorSpeed = 0.5f;
        public float maxAnimatorSpeed = 1.8f;

        [Tooltip("����ѡ��ͬ��д�� Animator ��ĳ�� float ���������� RunSpeed��������д��")]
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
            // �뱾��������� mover/driver �� UnitRef �Ƚ�����
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

            // HexClickMover ÿһ���� stepSeconds������= path.Count-1
            float time = Mathf.Max(0.01f, mover.stepSeconds * (path.Count - 1));
            float v = dist / time; // ʵ�������ٶȣ���/�룩

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
