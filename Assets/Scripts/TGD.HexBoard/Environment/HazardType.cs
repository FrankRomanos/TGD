// File: TGD.HexBoard/Environment/HazardType.cs
using UnityEngine;

namespace TGD.HexBoard
{
    public enum HazardKind { Trap, Pit }

    [CreateAssetMenu(menuName = "TGD/HexBoard/Hazard Type")]
    public class HazardType : ScriptableObject
    {
        public string hazardId = "Trap";
        public HazardKind kind = HazardKind.Trap;

        [Header("VFX (optional)")]
        public GameObject vfxPrefab;

        [Header("Tick (per turn)")]
        [Tooltip("ÿ�غ���ഥ�����Σ�-1 ��ʾ���޴�")]
        public int tickTimes = -1;

        [Tooltip("�ɵ������ޣ�-1 ��ʾ���޲�")]
        public int maxStacks = -1;

        // ====== Trap ����Ч����������Լ��٣��˺��������룩======
        [Header("Trap Effects (optional)")]
        [Tooltip("���� Trap ʱ���ŵ����ٱ��ʣ�<1=���٣�>1=���٣�һ�������� <1��")]
        [Range(0.1f, 3f)] public float stickyMoveMult = 1f;

        [Tooltip("���ų����غ�����<=0 ��ʾ������")]
        public int stickyDurationTurns = 0;

        // Ԥ���������˺���ÿ�غ��˺���
        // public int onEnterDamage = 0;
        // public int perTurnDamage = 0;
    }
}
