using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public static class ActionPhaseLogger
    {
        public static void Log(Unit unit, string toolId, ActionPhase phase, string message = null)
        {
            var label = TurnManagerV2.FormatUnitLabel(unit);
            string suffix = string.IsNullOrEmpty(message) ? string.Empty : $" {message}";
            Debug.Log($"[Action] {label} [{toolId}] {phase}{suffix}");
        }
    }
}