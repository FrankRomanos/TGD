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

        public static void LogFullRoundDeclared(Unit unit, string toolId, int delay)
        {
            var label = TurnManagerV2.FormatUnitLabel(unit);
            Debug.Log($"[Action] {label} [{toolId}] FullRound_Declared (delay={delay})");
        }

        public static void LogFullRoundExecute(Unit unit, string toolId)
        {
            var label = TurnManagerV2.FormatUnitLabel(unit);
            Debug.Log($"[FullRound] Execute {label} [{toolId}]");
        }
    }
}