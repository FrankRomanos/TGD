using UnityEngine;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class RefreshCooldownOnCast : MonoBehaviour
    {
        public string triggerActionId = "SK_A";
        public string targetActionId = "SK_B";

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

        void OnResolved(UnitRuntimeContext casterCtx, string actionId)
        {
            if (_ctx == null || casterCtx != _ctx)
                return;
            if (!ReduceCooldownOnCast.Matches(triggerActionId, actionId))
                return;

            var hub = _ctx.cooldownHub;
            if (hub == null || hub.secStore == null)
                return;
            if (string.IsNullOrEmpty(targetActionId))
                return;

            int before = hub.secStore.SecondsLeft(targetActionId);
            if (before <= 0)
                return;

            hub.secStore.StartSeconds(targetActionId, 0);
            ActionPhaseLogger.Log($"[Rules] CD reduce: {targetActionId} {before}->0 (Cast {triggerActionId})");
        }
    }
}
