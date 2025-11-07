using UnityEngine;
using TGD.CoreV2;
using TGD.CombatV2.Targeting;

namespace TGD.CombatV2
{
    public sealed class TestReactionAction40 : ChainActionBase
    {
        public override ActionKind Kind => ActionKind.Reaction;

        void Reset()
        {
            skillId = "Reaction40";
            timeCostSeconds = 2;
            energyCost = 40;
            targetRule = TargetRule.EnemyOrGround;
            cooldownSeconds = 18;
        }
    }
}
