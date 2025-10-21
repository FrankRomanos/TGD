using UnityEngine;
using TGD.CombatV2.Targeting;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public sealed class TestEnemyStandardAction : ChainTestActionBase, IActionResolveEffect
    {
        [SerializeField]
        string resolveLog = "test chain";

        public override ActionKind Kind => ActionKind.Standard;

        void Reset()
        {
            actionId = "EnemyTestStandard";
            timeCostSeconds = 1;
            energyCost = 0;
            targetMode = TargetMode.SelfOnly;
            cooldownSeconds = 0;
        }

        public void OnResolve(Unit unit, Hex target)
        {
            if (!string.IsNullOrEmpty(resolveLog))
                Debug.Log(resolveLog);
        }
    }
}
