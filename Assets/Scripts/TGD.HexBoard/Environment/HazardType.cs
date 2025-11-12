// File: TGD.HexBoard/Environment/HazardType.cs
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    public enum HazardKind { Trap, Pit, SpeedPatch, EntangleTrap }

    [CreateAssetMenu(menuName = "TGD/HexBoard/Hazard Type")]
    public class HazardType : ScriptableObject
    {
        public string hazardId = "Trap";
        public HazardKind kind = HazardKind.Trap;

        [Header("VFX (optional)")]
        public GameObject vfxPrefab;

        [Header("Tick (per turn)")]
        [Tooltip("ÿغഥΣ-1 ʾ޴")]
        public int tickTimes = -1;

        [Tooltip("ɵޣ-1 ʾ޲")]
        public int maxStacks = -1;

        // ====== Trap ЧԼ٣˺룩======
        [Header("Trap Effects (optional)")]
        [Tooltip(" Trap ʱŵٱʣ<1=٣>1=٣һ <1")]
        [Range(0.1f, 3f)] public float stickyMoveMult = 1f;

        [Tooltip("ųغ<=0 ʾ")]
        public int stickyDurationTurns = 0;

        [Header("Entangle Trap (optional)")]
        [Tooltip("单位踏入时施加缠绕的持续回合数（至少 1 回合）。")]
        [Min(1)] public int entangleDurationTurns = 1;

        [Tooltip("缠绕触发后是否摧毁该陷阱（从地形移除）。")]
        public bool destroyAfterEntangleTrigger = false;

        // Ԥ˺ÿغ˺
        // public int onEnterDamage = 0;
        // public int perTurnDamage = 0;

        [Header("Debug Hooks")]
        [Tooltip("When true, stepping onto this hazard prints a debug log message (prototype hook).")]
        public bool logOnEnter = true;

        [Tooltip("Optional custom debug log when a unit steps on this hazard. Supports {unit}, {hazard}, {hex}, {mult}.")]
        [TextArea]
        public string onEnterLogMessage;

        public void EmitEnterLog(Unit unit, Hex hex)
        {
            if (!logOnEnter)
                return;

            string hazardLabel = !string.IsNullOrEmpty(hazardId) ? hazardId : name;
            string unitLabel = unit != null
                ? (!string.IsNullOrEmpty(unit.Id) ? unit.Id : unit.ToString())
                : "<null unit>";
            string hexLabel = hex.ToString();
            string multiplierLabel = stickyMoveMult.ToString("0.###");

            string message = string.IsNullOrEmpty(onEnterLogMessage)
                ? "[Hazard] {unit} triggered {hazard} at {hex} (×{mult})"
                : onEnterLogMessage;

            message = message
                .Replace("{unit}", unitLabel)
                .Replace("{hazard}", hazardLabel)
                .Replace("{hex}", hexLabel)
                .Replace("{mult}", multiplierLabel);

            Debug.Log(message, this);
        }
    }
}
