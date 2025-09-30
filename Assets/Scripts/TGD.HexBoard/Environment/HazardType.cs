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
        [Tooltip("每回合最多触发几次；-1 表示无限次")]
        public int tickTimes = -1;

        [Tooltip("可叠层上限；-1 表示无限层")]
        public int maxStacks = -1;

        // ====== Trap 常见效果（先用黏性减速；伤害后续接入）======
        [Header("Trap Effects (optional)")]
        [Tooltip("进入 Trap 时附着的移速倍率（<1=减速，>1=加速；一般陷阱用 <1）")]
        [Range(0.1f, 3f)] public float stickyMoveMult = 1f;

        [Tooltip("附着持续回合数；<=0 表示不附着")]
        public int stickyDurationTurns = 0;

        // 预留：进入伤害、每回合伤害等
        // public int onEnterDamage = 0;
        // public int perTurnDamage = 0;
    }
}
