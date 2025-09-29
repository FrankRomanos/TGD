// File: TGD.CombatV2/AttackActionConfigV2.cs
using UnityEngine;

namespace TGD.CombatV2
{
    [CreateAssetMenu(menuName = "TGD/CombatV2/Attack Action Config")]
    public class AttackActionConfigV2 : ScriptableObject
    {
        [Header("Timing / Budget")]
        [Tooltip("���ι��������ڡ�λ�ơ���ʱ��Ԥ�㣨�룬ȡ����")]
        public int baseTimeSeconds = 2;               // ����2 / 3 / 1

        [Tooltip("�����ۼƽ�ʡ �� ��ֵ���� 1s")]
        [Range(0.1f, 1.0f)] public float refundThresholdSeconds = 0.8f;

        [Header("Energy Cost")]
        [Tooltip("�����������ģ�����2s=20��3s=30��1s=5��")]
        public int baseEnergyCost = 20;

        [Tooltip("���غϵڶ�����ÿ�� +50% �������ģ��ɹر�")]
        public bool applySameTurnPenalty = true;

        [Tooltip("ͬ�غϵ���ϵ����Ĭ�� 0.5 = +50%/�Σ�")]
        [Range(0f, 1f)] public float sameTurnPenaltyRate = 0.5f;

        [Header("Reach")]
        [Tooltip("��ս��ࣨ1=���ڣ�")]
        public int meleeRange = 1;

        [Header("Facing")]
        public float keepDeg = 45f;
        public float turnDeg = 135f;
        public float turnSpeedDegPerSec = 720f;
    }
}
