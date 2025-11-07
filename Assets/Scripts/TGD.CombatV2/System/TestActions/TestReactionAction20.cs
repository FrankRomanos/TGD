using UnityEngine;
using TGD.CoreV2;
using TGD.CombatV2.Targeting;

namespace TGD.CombatV2
{
    public sealed class TestReactionAction20 : ChainActionBase
    {
        public override ActionKind Kind => ActionKind.Reaction;

        void Reset()
        {
            skillId = "Reaction20";
            timeCostSeconds = 1;
            energyCost = 20;
            targetRule = TargetRule.EnemyOrGround;
            cooldownSeconds = 12;
        }
    }
}
