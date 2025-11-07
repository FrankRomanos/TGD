using UnityEngine;
using TGD.CombatV2.Targeting;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    public sealed class TestEnemyStandardAction : ChainActionBase, IActionResolveEffect
    {
        [SerializeField]
        string resolveLog = "test chain";

        public override ActionKind Kind => ActionKind.Standard;

        void Reset()
        {
            skillId = "EnemyTestStandard";
            timeCostSeconds = 1;
            energyCost = 0;
            targetRule = TargetRule.SelfOnly;
            cooldownSeconds = 0;
        }

        public void OnResolve(Unit unit, Hex target)
        {
            if (!string.IsNullOrEmpty(resolveLog))
                Debug.Log(resolveLog);
        }
    }
}
