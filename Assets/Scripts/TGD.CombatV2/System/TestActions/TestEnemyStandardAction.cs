using System.Collections;
using UnityEngine;
using TGD.CombatV2.Targeting;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public sealed class TestEnemyStandardAction : ChainTestActionBase
    {
        public override ActionKind Kind => ActionKind.Standard;

        [Header("Debug")]
        public bool logPhases = true;

        void Reset()
        {
            actionId = "EnemyStandard";
            timeCostSeconds = 3;
            energyCost = 0;
            targetMode = TargetMode.EnemyOrGround;
            cooldownSeconds = 0;
        }

        protected override void Awake()
        {
            base.Awake();
        }

        void LogPhase(string phase, Hex? hex = null)
        {
            if (!logPhases)
                return;

            var unit = ResolveUnit();
            var label = TurnManagerV2.FormatUnitLabel(unit);
            string suffix = hex.HasValue ? $" target={hex.Value}" : string.Empty;
            Debug.Log($"[EnemyStandard] {label} {phase}{suffix}", this);
        }

        public override void OnEnterAim()
        {
            base.OnEnterAim();
            LogPhase("W1_AimBegin");
        }

        public override IEnumerator OnConfirm(Hex hex)
        {
            LogPhase("W3_ExecuteBegin", hex);
            yield return base.OnConfirm(hex);
            LogPhase("W3_ExecuteEnd", hex);
        }

        public override void OnExitAim()
        {
            base.OnExitAim();
            LogPhase("W1_AimEnd");
        }
    }
}
