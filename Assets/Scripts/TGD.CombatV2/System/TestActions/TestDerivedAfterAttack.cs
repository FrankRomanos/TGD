using UnityEngine;
using TGD.CombatV2.Targeting;

namespace TGD.CombatV2
{
    public sealed class TestDerivedAfterAttack : ChainTestActionBase
    {
        public override ActionKind Kind => ActionKind.Derived;

        void Reset()
        {
            actionId = "DerivedAfterAttack";
            timeCostSeconds = 1;
            energyCost = 15;
            targetMode = TargetMode.EnemyOrGround;
            cooldownSeconds = 12;
        }
    }
}
