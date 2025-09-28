using UnityEngine;

namespace TGD.HexBoard
{
    public enum HazardKind { Trap, Pit } // 以后要扩再加

    [CreateAssetMenu(menuName = "TGD/HexBoard/Hazard Type")]
    public class HazardType : ScriptableObject
    {
        public string hazardId = "Trap";
        public HazardKind kind = HazardKind.Trap;

        [Header("VFX (optional)")]
        public GameObject vfxPrefab;

        [Header("Tick (per turn)")]
        [Tooltip("每回合最多触发几次；-1 表示无限次")]
        public int tickTimes = -1;     // ← 由 tickSeconds 改名为 tickTimes

        [Tooltip("可叠层上限；-1 表示无限层")]
        public int maxStacks = -1;     // ← 约定与技能解释器一致，-1=无上限
        // ……后续再扩 onEnterDamage / onTickDamage / slowPct 等
    }
}
