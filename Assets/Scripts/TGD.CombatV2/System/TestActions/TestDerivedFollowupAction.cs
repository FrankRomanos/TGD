using UnityEngine;
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
            targetMode = TargetMode.EnemyOrGround;
            cooldownSeconds = 8;
        }
    }
}
