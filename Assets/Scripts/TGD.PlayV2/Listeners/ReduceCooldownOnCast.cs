using UnityEngine;
using TGD.CoreV2;
using TGD.CombatV2;

namespace TGD.PlayV2
{
    [DisallowMultipleComponent]
    public sealed class ReduceCooldownOnCast : MonoBehaviour
    {
        public string triggerSkillId = "SK_A";
        public string targetSkillId = "SK_B";
        public int reduceSeconds = 6;

        UnitRuntimeContext _ctx;

        void OnEnable()
        {
            _ctx = GetComponentInParent<UnitRuntimeContext>(true);
            CAM.ActionResolved += OnResolved;
        }

        void OnDisable()
        {
            CAM.ActionResolved -= OnResolved;
        }

        void OnResolved(UnitRuntimeContext casterCtx, string skillId)
        {
            if (_ctx == null || casterCtx != _ctx)
                return;
            if (!Matches(triggerSkillId, skillId))
                return;

            var hub = _ctx.cooldownHub;
            if (hub == null || hub.secStore == null)
                return;
            if (string.IsNullOrEmpty(targetSkillId))
                return;

            int before = hub.secStore.SecondsLeft(targetSkillId);
            int delta = Mathf.Max(0, reduceSeconds);
            int after = Mathf.Max(0, before - delta);
            if (after == before)
                return;

            hub.secStore.StartSeconds(targetSkillId, after);
            ActionPhaseLogger.Log($"[Rules] CD reduce: {targetSkillId} {before}->{after} (Cast {triggerSkillId})");
        }

        internal static bool Matches(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(value))
                return false;
            if (pattern.EndsWith("*"))
            {
                var prefix = pattern.Substring(0, pattern.Length - 1);
                return value.StartsWith(prefix, System.StringComparison.Ordinal);
            }
            if (pattern.EndsWith("_"))
                return value.StartsWith(pattern, System.StringComparison.Ordinal);
            return string.Equals(pattern, value, System.StringComparison.Ordinal);
        }
    }
}
