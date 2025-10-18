using UnityEngine;
using TGD.CombatV2.Targeting;

namespace TGD.CombatV2
{
    public sealed class TestDerivedFollowupAction : ChainTestActionBase
    {
        public override ActionKind Kind => ActionKind.Derived;

        void Reset()
        {
            actionId = "DerivedAfterDerived";
            timeCostSeconds = 1;
            energyCost = 10;
            targetMode = TargetMode.EnemyOrGround;
            cooldownSeconds = 8;
        }
    }
}
