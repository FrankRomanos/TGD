using UnityEngine;
using TGD.CombatV2.Targeting;

namespace TGD.CombatV2
{
    public sealed class TestDerivedAfterAttack : ChainActionBase
    {
        public override ActionKind Kind => ActionKind.Derived;

        void Reset()
        {
            skillId = "DerivedAfterAttack";
            timeCostSeconds = 1;
            energyCost = 15;
            targetMode = TargetMode.EnemyOrGround;
            cooldownSeconds = 12;
        }
    }
}
