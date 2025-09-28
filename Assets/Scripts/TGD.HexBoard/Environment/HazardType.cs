using UnityEngine;

namespace TGD.HexBoard
{
    public enum HazardKind { Trap, Pit } // �Ժ�Ҫ���ټ�

    [CreateAssetMenu(menuName = "TGD/HexBoard/Hazard Type")]
    public class HazardType : ScriptableObject
    {
        public string hazardId = "Trap";
        public HazardKind kind = HazardKind.Trap;

        [Header("VFX (optional)")]
        public GameObject vfxPrefab;

        [Header("Tick (per turn)")]
        [Tooltip("ÿ�غ���ഥ�����Σ�-1 ��ʾ���޴�")]
        public int tickTimes = -1;     // �� �� tickSeconds ����Ϊ tickTimes

        [Tooltip("�ɵ������ޣ�-1 ��ʾ���޲�")]
        public int maxStacks = -1;     // �� Լ���뼼�ܽ�����һ�£�-1=������
        // ������������ onEnterDamage / onTickDamage / slowPct ��
    }
}
