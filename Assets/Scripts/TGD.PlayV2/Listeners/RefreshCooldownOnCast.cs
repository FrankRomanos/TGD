using UnityEngine;
using TGD.CoreV2;
using TGD.CombatV2;

namespace TGD.PlayV2
{
    [DisallowMultipleComponent]
    public sealed class RefreshCooldownOnCast : MonoBehaviour
    {
        public string triggerSkillId = "SK_A";
        public string targetSkillId = "SK_B";

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
            if (!ReduceCooldownOnCast.Matches(triggerSkillId, skillId))
                return;

            var hub = _ctx.cooldownHub;
            if (hub == null || hub.secStore == null)
                return;
            if (string.IsNullOrEmpty(targetSkillId))
                return;

            int before = hub.secStore.SecondsLeft(targetSkillId);
            if (before <= 0)
                return;

            hub.secStore.StartSeconds(targetSkillId, 0);
            ActionPhaseLogger.Log($"[Rules] CD reduce: {targetSkillId} {before}->0 (Cast {triggerSkillId})");
        }
    }
}
