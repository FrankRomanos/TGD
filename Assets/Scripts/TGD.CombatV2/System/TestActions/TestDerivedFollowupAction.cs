using UnityEngine;
using TGD.CoreV2;
using TGD.CombatV2.Targeting;

namespace TGD.CombatV2
{
    public sealed class TestDerivedFollowupAction : ChainActionBase
    {
        public override ActionKind Kind => ActionKind.Derived;

        void Reset()
        {
            skillId = "DerivedAfterDerived";
            timeCostSeconds = 1;
            energyCost = 10;
            targetRule = TargetRule.EnemyOrGround;
            cooldownSeconds = 8;
        }
    }
}
