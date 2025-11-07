using UnityEngine;
using TGD.CoreV2;
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
            targetRule = TargetRule.EnemyOrGround;
            cooldownSeconds = 12;
        }
    }
}
